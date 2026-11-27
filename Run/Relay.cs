using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Proctor;

/// <summary>What a run relays to its agents, as its log entry records it: the directory holding the sockets and the ports of this machine behind them.</summary>
record ResolvedRelay(string Dir, List<int> Ports);

/// <summary>
/// The host end of the relay (project/plans/container-network-finish.md, "A port on this machine"): for each port, a
/// unix socket in one short directory, every connection to it passed to 127.0.0.1:&lt;port&gt; on this machine. A compiled
/// task mounts the directory into a service beside the agent (Harbor/relay.py), which serves the same ports at the
/// agent's localhost. A socket is a file, so it crosses into the container as a mount and no network path is opened:
/// the host's loopback stays shut to containers, nothing is bound to the network, and the agent's phase needs no host
/// allowed. Bytes only, in both directions; nothing is read, and no key is held.
/// </summary>
sealed class HostRelay : IDisposable
{
    readonly string dir;
    readonly List<Socket> listeners = [];
    readonly CancellationTokenSource stop = new();

    HostRelay(string dir) => this.dir = dir;

    /// <summary>
    /// Where a run's sockets live: the user's runtime directory (the temp directory without one), by a short name of the
    /// log entry's id, since a unix socket's path is at most 108 bytes.
    /// </summary>
    public static string Dir(string entryId) => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } run ? run : Path.GetTempPath(),
        "proctor-relay", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(entryId)))[..12]);

    public static string SocketName(int port) => $"{port}.sock";

    /// <summary>Whether a host is this machine's loopback: localhost, a .localhost name, 127.0.0.0/8 or ::1.</summary>
    public static bool Loopback(string host)
    {
        host = host.Trim('[', ']').ToLowerInvariant();
        return host == "localhost" || host.EndsWith(".localhost") || (IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip));
    }

    /// <summary>Whether anything answers at 127.0.0.1:&lt;port&gt; on this machine now.</summary>
    public static bool Answers(int port)
    {
        try
        {
            using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            return probe.ConnectAsync(IPAddress.Loopback, port).Wait(TimeSpan.FromSeconds(2)) && probe.Connected;
        }
        catch (Exception e) when (e is SocketException or AggregateException) { return false; }
    }

    /// <summary>Listen on each port's socket in the directory, which is made new: what a crashed run left there is removed.</summary>
    public static HostRelay Start(string dir, IEnumerable<int> ports)
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var relay = new HostRelay(dir);
        foreach (var port in ports)
        {
            var path = Path.Combine(dir, SocketName(port));
            if (Encoding.UTF8.GetByteCount(path) > 107) throw new ProctorException($"the relay's socket path is too long for a unix socket ({path}): set XDG_RUNTIME_DIR to a shorter directory");
            var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(path));
            listener.Listen(64);
            relay.listeners.Add(listener);
            _ = relay.Accept(listener, port);
        }
        return relay;
    }

    async Task Accept(Socket listener, int port)
    {
        try
        {
            while (true) _ = Carry(await listener.AcceptAsync(stop.Token), port);
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException) { }
    }

    async Task Carry(Socket near, int port)
    {
        using var _ = near;
        using var far = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await far.ConnectAsync(IPAddress.Loopback, port, stop.Token);
            await System.Threading.Tasks.Task.WhenAll(Pump(near, far), Pump(far, near));
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException or IOException) { }
    }

    async Task Pump(Socket from, Socket to)
    {
        var buffer = new byte[65536];
        try
        {
            while (await from.ReceiveAsync(buffer, SocketFlags.None, stop.Token) is > 0 and var n)
                for (var sent = 0; sent < n;) sent += await to.SendAsync(buffer.AsMemory(sent, n - sent), SocketFlags.None, stop.Token);
        }
        catch (Exception e) when (e is SocketException or IOException) { }
        finally
        {
            try { to.Shutdown(SocketShutdown.Send); } catch (Exception e) when (e is SocketException or ObjectDisposedException) { }
        }
    }

    public void Dispose()
    {
        stop.Cancel();
        foreach (var listener in listeners) listener.Dispose();
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }
}
