using System.Text.Json;
using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

public class FrictionTests
{
    // A history tool under test, with git and gh as the ordinary ways round it; an empty answer or an error is a failed call.
    static readonly Surface History = new(new SurfaceDef(
        Tools: ["mcp__gh-history__*"], Docs: ["/app/docs/gh-history.md"], Fails: [@"^\[\]$", "\"error\""],
        Substitutes: new SubstitutesDef(Commands: ["git log", "git blame", "git show", "gh"])));

    static (string, JsonObject, string) Tool(string result = "[]") => ("mcp__gh-history__timeline", new JsonObject { ["path"] = "src/parser.c" }, result);
    static (string, JsonObject, string) Edit => ("Edit", new JsonObject { ["file_path"] = "/app/src/parser.c", ["old_string"] = "a", ["new_string"] = "b" }, "ok");

    internal static (string, JsonObject, string) Bash(string command, string result = "") => ("Bash", new JsonObject { ["command"] = command }, result);

    internal static JsonObject Trajectory(params (string Tool, JsonObject Arguments, string Result)[][] steps) => new()
    {
        ["schema_version"] = "ATIF-v1.7",
        ["agent"] = new JsonObject { ["name"] = "test" },
        ["steps"] = new JsonArray(steps.Select((calls, i) => (JsonNode?)new JsonObject
        {
            ["step_id"] = i + 1, ["source"] = "agent", ["message"] = "", ["extra"] = new JsonObject { ["cwd"] = "/app" }, ["metrics"] = new JsonObject { ["cost_usd"] = 0.01 },
            ["tool_calls"] = new JsonArray(calls.Select((c, n) => (JsonNode?)new JsonObject { ["tool_call_id"] = $"c{i}_{n}", ["function_name"] = c.Tool, ["arguments"] = c.Arguments.DeepClone() }).ToArray()),
            ["observation"] = new JsonObject { ["results"] = new JsonArray(calls.Select((c, n) => (JsonNode?)new JsonObject { ["source_call_id"] = $"c{i}_{n}", ["content"] = c.Result }).ToArray()) },
        }).ToArray()),
    };

    static JsonObject Detect(Surface surface, params (string Tool, JsonObject Arguments, string Result)[][] steps) =>
        Friction.Detect(Trajectory(steps), surface, new JsonObject { ["task"] = "t" }).File;

    static string[] LabelsOf(JsonObject file) => file["points"]!.AsArray().Select(p => p!["label"]!.GetValue<string>()).Distinct().Order().ToArray();
    static JsonObject SurfaceOf(JsonObject file) => file["summary"]!["surface"]!.AsObject();

    [Fact]
    public void ARunThatUsesTheSurface_HasNoPoints()
    {
        var file = Detect(History, [Bash("cat docs/gh-history.md", "usage")], [Tool("""[{"sha":"abc"}]""")], [Edit], [Bash("make test", "ok")]);

        Assert.Empty(file["points"]!.AsArray());
        var surface = SurfaceOf(file);
        Assert.True(surface["reached"]!.GetValue<bool>());
        Assert.Equal("surface", surface["first_reach"]!["reached"]!.GetValue<string>());
        Assert.True(surface["worked"]!.GetValue<bool>());
        Assert.False(surface["used_substitute"]!.GetValue<bool>());
        Assert.Equal(1, surface["parts"]!["about"]!["actions"]!.GetValue<int>());
    }

    [Fact]
    public void ASubstituteAfterAFailedSurfaceCall_IsAbandoned()
    {
        var file = Detect(History, [Tool("[]")], [Bash("git log --oneline -- src/parser.c | head", "abc fix")], [Edit]);

        Assert.Equal(["abandoned", "substitute"], LabelsOf(file));
        var abandoned = file["points"]!.AsArray().Single(p => p!["label"]!.GetValue<string>() == "abandoned")!;
        Assert.Equal(["1", "2"], abandoned["steps"]!.AsArray().Select(s => s!.GetValue<string>()));
        Assert.Equal("[]", abandoned["evidence"]![0]!["quote"]!.GetValue<string>());
        Assert.Equal("result", abandoned["evidence"]![0]!["where"]!.GetValue<string>());
        Assert.False(SurfaceOf(file)["worked"]!.GetValue<bool>());
        Assert.Equal(1, SurfaceOf(file)["surface_calls_failed"]!.GetValue<int>());
    }

    [Fact]
    public void ASubstituteAfterAWorkingSurfaceCall_IsACheapCheck_NotAbandoned()
    {
        var file = Detect(History, [Tool("""[{"sha":"abc"}]""")], [Bash("git log -1 abc", "abc fix")], [Edit]);

        Assert.Equal(["substitute"], LabelsOf(file));
    }

    [Fact]
    public void ARunThatNeverTouchesTheSurface_WentRound()
    {
        var file = Detect(History, [Bash("cd /app && git blame src/parser.c | head -20", "abc")], [Bash("gh pr view 12", "title")], [Edit]);

        Assert.Equal(["substitute"], LabelsOf(file));
        Assert.Equal("substitute", SurfaceOf(file)["first_reach"]!["reached"]!.GetValue<string>());
        Assert.False(SurfaceOf(file)["used"]!.GetValue<bool>());
    }

    [Fact]
    public void ASubstituteLongAfterAFailedCall_IsNotAbandoned()
    {
        var file = Detect(History, [Tool("[]")], [Edit], [Bash("make test", "ok")], [Edit], [Bash("git log -1", "abc")]);

        Assert.Equal(["substitute"], LabelsOf(file));
    }

    [Fact]
    public void WhatTheMachineRefuses_IsFoundWithNoDeclaration()
    {
        var file = Detect(Surface.None, [Bash("ls", "src")], [Bash("make", "make: gcc: command not found")], [Bash("apt-get install -y gcc", "E: Permission denied")]);

        Assert.Equal(["environment"], LabelsOf(file));
        Assert.Null(file["summary"]!["surface"]);
        Assert.Null(file["detector"]!["surface"]);
        Assert.Equal("make: gcc: command not found", file["points"]![0]!["evidence"]![0]!["quote"]!.GetValue<string>());
    }

    [Fact]
    public void ACommandThatOnlySharesAName_IsNotASubstitute()
    {
        var file = Detect(History, [Bash("git status --short")], [Bash("git diff")], [Tool("""[{"sha":"abc"}]""")]);

        Assert.Empty(file["points"]!.AsArray());
    }

    [Fact]
    public void AHostIsASurface_AndAnotherHostItsSubstitute()
    {
        var docs = new Surface(new SurfaceDef(Hosts: ["localhost:8080"], Fails: ["<title>Not found</title>"], Substitutes: new SubstitutesDef(Hosts: ["localhost:8081"])));

        var file = Detect(docs,
            [Bash("curl -s localhost:8080/quickstart", "<!doctype html><title>Not found</title>")],
            [Bash("# guess the endpoint\ncurl -si -X POST localhost:8081/v1/quotes -d '{}'", "HTTP/1.1 200 OK")]);

        Assert.Equal(["abandoned", "substitute"], LabelsOf(file));
        Assert.Equal(1, SurfaceOf(file)["parts"]!["substitute"]!["actions"]!.GetValue<int>());
    }

    [Fact]
    public void TheSameRun_GivesTheSameFile()
    {
        (string, JsonObject, string)[][] steps = [[Tool("[]")], [Bash("git log --oneline | head", "abc fix")], [Bash("python3 -c 'print(1)'", "bash: line 1: python3: command not found")]];

        Assert.Equal(Detect(History, steps).ToJsonString(), Detect(History, steps).ToJsonString());
    }

    [Theory]
    [InlineData("cd /app && git log --oneline | head; echo 'a b' \"c \\\" d\" e\\ f", "cd /app\ngit log --oneline\necho [a b] [c \" d] [e f]")]
    [InlineData("# a comment line\npython3 << 'EOF'\nprint(1)\nEOF\ncurl -s localhost:8081/x", "python3 << HEREDOC\ncurl -s localhost:8081/x")]
    [InlineData("sudo FOO=1 timeout 30 make test 2>&1 | tail -3", "make test 2 >& 1")]
    [InlineData("for f in a b; do cat $f; done", "f in a b\ncat $f")]
    [InlineData("echo 'left open", "echo 'left open")]
    public void ACommand_SplitsIntoOneActionPerPipeline(string command, string expected)
    {
        var actions = Shell.Actions(command).Select(a => string.Join(' ', a.Select(w => w.Contains(' ') ? $"[{w}]" : w)));

        Assert.Equal(expected, string.Join('\n', actions));
    }

    [Theory]
    [InlineData("/opt/tool/docs/a/b.md", "/opt/tool/docs/*", true)]
    [InlineData("/opt/tool/src/x.cs", "/opt/tool/docs/*", false)]
    [InlineData("mcp__gh-history__timeline", "mcp__gh-history__*", true)]
    public void AGlob_CrossesDirectories(string text, string glob, bool matches) => Assert.Equal(matches, Shell.Matches(text, glob));

    [Theory]
    [InlineData("/app/../opt//tool/./docs", "/opt/tool/docs")]
    [InlineData("a/../../b", "../b")]
    [InlineData("", ".")]
    public void APath_IsNormalisedAsPosix(string path, string expected) => Assert.Equal(expected, Shell.Normalise(path));

    [Fact]
    public void AFailsPatternThatIsNotARegex_IsAProblem()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.EditJson("smoke/exam.json", e => e["surface"] = new JsonObject { ["commands"] = new JsonArray("jot"), ["fails"] = new JsonArray("(unclosed") });
        var problems = new List<Problem>();

        Exam.Load(repo.Root, "smoke", problems);

        Assert.Contains(problems, p => p.Field == "surface.fails[0]");
    }

    [Fact]
    public void Friction_WritesFrictionJsonBesideEachCompletedAttemptsTrajectory()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.OneVariantOneAttempt();
        var problems = new List<Problem>();
        var config = Exam.LoadConfig(repo.Root, problems);
        var exam = repo.LoadExam("smoke");
        var id = Runner.Run(repo.Root, exam, config, null, null, "test", TextWriter.Null)!;
        var expDir = Layout.Batch(repo.Root, id);
        File.WriteAllText(Path.Combine(Layout.Attempt(expDir, "a", "loops", 1), "status"), "failed\n");

        var attempts = Friction.Attempts(repo.Root, exam, Pool.Select(repo.Root, exam).Current, TextWriter.Null);

        Assert.All(attempts, c => Assert.Equal("a", c.Variant));
        Assert.Equal("not completed", attempts.Single(c => c.Task == "loops").Skipped);
        Assert.False(File.Exists(Path.Combine(Layout.Attempt(expDir, "a", "loops", 1), Layout.FrictionFile)));
        var read = attempts.Single(c => c.Task == "uses-bash");
        Assert.NotNull(read.Points);
        var file = JsonNode.Parse(File.ReadAllText(Path.Combine(Layout.Attempt(expDir, "a", "uses-bash", 1), Layout.FrictionFile)))!;
        Assert.Equal(Friction.Schema, file["schema"]!.GetValue<string>());
        Assert.Equal("uses-bash", file["attempt"]!["task"]!.GetValue<string>());
        Assert.Equal("1", file["attempt"]!["attempt"]!.GetValue<string>());
        Assert.Equal(id, file["attempt"]!["batch"]!.GetValue<string>());
        Assert.True(file["summary"]!["calls"]!.GetValue<int>() > 0);

        // Once the variant is renamed its attempts are stale: listed with why, never read.
        repo.RenameVariant("smoke", "a", "renamed");
        var renamed = repo.LoadExam("smoke");
        var selection = Pool.Select(repo.Root, renamed);
        Assert.Empty(selection.Current);
        Assert.All(selection.Stale, s => Assert.Equal("variant a has no file now", s.Why));
        Assert.Equal(3, selection.Stale.Count);
    }
}
