using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Proctor.Tests;

/// <summary>The relay: a port of the machine that runs proctor at the agent's localhost (Run/Relay.cs, Harbor/relay.py), and what compile writes for it.</summary>
public class RelayTests
{
    /// <summary>The host end passes a connection on its unix socket to the port on the loopback and back, and ends each direction when its sender does.</summary>
    [Fact]
    public async Task HostEnd_CarriesBytesBothWays_BetweenItsSocketAndThePort()
    {
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var port = ((IPEndPoint)server.LocalEndpoint).Port;
        var served = Task.Run(async () =>
        {
            using var peer = await server.AcceptSocketAsync();
            var buffer = new byte[1024];
            var heard = new StringBuilder();
            while (await peer.ReceiveAsync(buffer) is > 0 and var n) heard.Append(Encoding.UTF8.GetString(buffer, 0, n));
            await peer.SendAsync(Encoding.UTF8.GetBytes($"heard {heard}"));
        });
        var dir = Path.Combine(Path.GetTempPath(), "prt-" + Guid.NewGuid().ToString("N")[..8]);
        using (HostRelay.Start(dir, [port]))
        {
            using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await client.ConnectAsync(new UnixDomainSocketEndPoint(Path.Combine(dir, HostRelay.SocketName(port))));
            await client.SendAsync(Encoding.UTF8.GetBytes("a request"));
            client.Shutdown(SocketShutdown.Send);
            var buffer = new byte[1024];
            var answer = new StringBuilder();
            while (await client.ReceiveAsync(buffer) is > 0 and var n) answer.Append(Encoding.UTF8.GetString(buffer, 0, n));
            Assert.Equal("heard a request", answer.ToString());
            await served;
        }
        Assert.False(Directory.Exists(dir));
    }

    /// <summary>A port answers when something listens on this machine's loopback there, and not otherwise.</summary>
    [Fact]
    public void Answers_SaysWhetherAnythingListens()
    {
        var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var port = ((IPEndPoint)server.LocalEndpoint).Port;
        Assert.True(HostRelay.Answers(port));
        server.Stop();
        Assert.False(HostRelay.Answers(port));
    }

    [Theory]
    [InlineData("localhost", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("127.8.0.1", true)]
    [InlineData("[::1]", true)]
    [InlineData("api.localhost", true)]
    [InlineData("library", false)]
    [InlineData("10.0.0.5", false)]
    [InlineData("api.anthropic.com", false)]
    public void Loopback_IsThisMachineOnly(string host, bool loopback) => Assert.Equal(loopback, HostRelay.Loopback(host));

    /// <summary>A model on this machine's loopback is relayed: the task gets the relay's service and script, the agent's phase needs no host, and the fixture's own services stay.</summary>
    [Fact]
    public void Compile_RelaysAModelOnThisMachine_AndGivesTheAgentNoNetwork()
    {
        using var repo = new TestRepo();
        repo.CopyExam("conformance");
        var entryDir = Path.Combine(repo.Root, "log", "exp");
        HarborCompile.Tasks(entryDir, repo.LoadExam("conformance"), _ => new AgentRoute("http://localhost:8086/v1", "key"), relayDir: "/run/user/1000/proctor-relay/abc");
        var task = Layout.HarborTask(entryDir, "codex", "ok");

        var compose = File.ReadAllText(Path.Combine(task, "environment", "docker-compose.yaml")).Split('\n');
        var services = Array.IndexOf(compose, "services:");
        Assert.Equal("  main: { cap_drop: [NET_RAW] }", compose[services + 1]);
        Assert.Equal("  proctor-relay: { image: \"python:3.13-alpine\", command: [\"python\", \"-u\", \"/proctor-relay.py\", \"/run/proctor-relay\", \"8086\"], volumes: [\"./proctor-relay.py:/proctor-relay.py:ro\", \"/run/user/1000/proctor-relay/abc:/run/proctor-relay\"] }", compose[services + 2]);
        Assert.Contains("  stub:", compose);
        Assert.True(File.Exists(Path.Combine(task, "environment", "proctor-relay.py")));
        var toml = File.ReadAllText(Path.Combine(task, "task.toml"));
        Assert.DoesNotContain("allowed_hosts", toml);
        Assert.Equal(2, toml.Split("network_mode = \"no-network\"").Length - 1 - 1);   // the agent's and the environment's; the verifier's is the third
    }

    /// <summary>A loopback port the fixture's own services answer on (image.serves) is the fixture's, not this machine's; and with no relay held (a remote engine) nothing is relayed.</summary>
    [Fact]
    public void Compile_LeavesAPortTheFixtureServes_AndARunWithNoRelay()
    {
        using var repo = new TestRepo();
        repo.CopyExam("conformance");
        var exam = repo.LoadExam("conformance");
        var served = Path.Combine(repo.Root, "log", "served");
        HarborCompile.Tasks(served, exam, _ => new AgentRoute("http://localhost:8765/v1", "stub"), relayDir: "/run/x");
        var none = Path.Combine(repo.Root, "log", "none");
        HarborCompile.Tasks(none, exam, _ => new AgentRoute("http://localhost:8086/v1", "key"));

        foreach (var entryDir in new[] { served, none })
        {
            var task = Layout.HarborTask(entryDir, "codex", "ok");
            Assert.DoesNotContain("proctor-relay", File.ReadAllText(Path.Combine(task, "environment", "docker-compose.yaml")));
            Assert.False(File.Exists(Path.Combine(task, "environment", "proctor-relay.py")));
            Assert.Contains("allowed_hosts = [\"localhost\"]", File.ReadAllText(Path.Combine(task, "task.toml")));
        }
    }

    /// <summary>A fixture with no services of its own gets a compose file for the relay alone, with NET_RAW dropped from the agent beside it.</summary>
    [Fact]
    public void Compile_WritesAComposeFile_ForAFixtureWithNoServices()
    {
        using var repo = new TestRepo();
        repo.CopyExam("harbor-unit");
        var exam = repo.LoadExam("harbor-unit");
        var entryDir = Path.Combine(repo.Root, "log", "exp");
        HarborCompile.Tasks(entryDir, exam, _ => new AgentRoute("http://127.0.0.1:8086", "key"), relayDir: "/run/x");
        var task = Layout.HarborTask(entryDir, "cc-haiku", "fix-null-deref");

        Assert.Equal(
            "services:\n  main: { cap_drop: [NET_RAW] }\n  proctor-relay: { image: \"python:3.13-alpine\", command: [\"python\", \"-u\", \"/proctor-relay.py\", \"/run/proctor-relay\", \"8086\"], volumes: [\"./proctor-relay.py:/proctor-relay.py:ro\", \"/run/x:/run/proctor-relay\"] }\n",
            File.ReadAllText(Path.Combine(task, "environment", "docker-compose.yaml")));
        Assert.Contains("[agent]\ntimeout_sec", File.ReadAllText(Path.Combine(task, "task.toml")));
        Assert.DoesNotContain("allowed_hosts", File.ReadAllText(Path.Combine(task, "task.toml")));
    }

    /// <summary>The fixture's ports are checked where it is loaded.</summary>
    [Fact]
    public void Fixture_RefusesAPortBothServedAndThisMachines()
    {
        using var repo = new TestRepo();
        repo.CopyExam("conformance");
        var file = Path.Combine(repo.Root, "fixtures", "stub", "fixture.json");
        File.WriteAllText(file, File.ReadAllText(file).Replace("\"serves\": [8765]", "\"serves\": [8765], \"host_ports\": [8765, 70000]"));
        var problems = repo.Problems("conformance").Select(p => $"{p.Field}: {p.Message}").ToList();
        Assert.Contains("image.host_ports: ports, each 1 to 65535", problems);
        Assert.Contains("image.host_ports: 8765 is in image.serves too: a port is the fixture's service or this machine's, not both", problems);
    }

    /// <summary>A fixture's further hosts join the model's in the agent's allowlist, and stand alone when the model is relayed.</summary>
    [Fact]
    public void Compile_AllowsTheHostsAFixtureNames_BesideTheModels()
    {
        using var repo = new TestRepo();
        repo.CopyExam("conformance");
        var file = Path.Combine(repo.Root, "fixtures", "stub", "fixture.json");
        File.WriteAllText(file, File.ReadAllText(file).Replace("\"serves\": [8765]", "\"serves\": [8765], \"allowed_hosts\": [\"api.example.com\", \"library\"]"));
        var exam = repo.LoadExam("conformance");
        var far = Path.Combine(repo.Root, "log", "far");
        HarborCompile.Tasks(far, exam, _ => new AgentRoute("http://library:8086/v1", "key"), relayDir: "/run/x");
        var near = Path.Combine(repo.Root, "log", "near");
        HarborCompile.Tasks(near, exam, _ => new AgentRoute("http://localhost:8086/v1", "key"), relayDir: "/run/x");

        Assert.Contains("[agent]\ntimeout_sec = 120.0\nnetwork_mode = \"allowlist\"\nallowed_hosts = [\"library\", \"api.example.com\"]\n", File.ReadAllText(Path.Combine(Layout.HarborTask(far, "codex", "ok"), "task.toml")));
        Assert.Contains("[agent]\ntimeout_sec = 120.0\nnetwork_mode = \"allowlist\"\nallowed_hosts = [\"api.example.com\", \"library\"]\n", File.ReadAllText(Path.Combine(Layout.HarborTask(near, "codex", "ok"), "task.toml")));

        File.WriteAllText(file, File.ReadAllText(file).Replace("\"api.example.com\"", "\"https://api.example.com:443/v1\""));
        Assert.Contains(repo.Problems("conformance"), p => p.Field == "image.allowed_hosts" && p.Message.StartsWith("'https://api.example.com:443/v1' is not a host"));
    }

    /// <summary>A host inside the range an engine gives its container networks is warned of, by address or by name, and no other.</summary>
    [Theory]
    [InlineData("docker", "172.17.1.20", true)]
    [InlineData("docker", "172.16.0.5", false)]
    [InlineData("docker", "10.88.0.5", false)]
    [InlineData("podman", "10.89.3.2", true)]
    [InlineData("podman", "172.17.1.20", false)]
    [InlineData("docker", "library", true)]
    [InlineData("docker", "api.example.com", false)]
    [InlineData("docker", "nowhere", false)]
    public void SubnetWarning_NamesAHostInsideTheEnginesDefaultRange(string engine, string host, bool warned)
    {
        IPAddress[] Resolve(string name) => name switch
        {
            "library" => [IPAddress.Parse("fe80::1"), IPAddress.Parse("172.20.0.9")],
            "api.example.com" => [IPAddress.Parse("93.184.216.34")],
            _ => throw new SocketException(11001),
        };
        var warning = HarborRun.SubnetWarning(engine, host, Resolve);
        Assert.Equal(warned, warning is not null);
        if (warned) Assert.StartsWith($"{host} is at ", warning);
    }
}
