using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

/// <summary>Step 5: every built-in has a passing and a failing case; negation; @expect; the script contract.</summary>
public class ChecksTests
{
    static Transcript Fixture(string name, string? diff = null) =>
        Transcript.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name + ".jsonl")), diff);

    static CellContext Cell(JsonObject? expect = null, string? evalDir = null, string? cellDir = null) =>
        new(evalDir ?? "/nonexistent", cellDir ?? Path.GetTempPath(), "/work", "exp", "a", new CaseDef("c", null, "p", expect), 1);

    static Verdict Eval(string spec, Transcript t, JsonObject? expect = null) =>
        Checks.Evaluate(JsonNode.Parse(spec)!.AsObject(), Cell(expect), t);

    const string Diff = "diff --git a/src/fetch.cs b/src/fetch.cs\n--- a/src/fetch.cs\n+++ b/src/fetch.cs\n@@ -1 +1 @@\n-a\n+b\ndiff --git a/README.md b/README.md\n--- a/README.md\n+++ b/README.md\n@@ -1 +1 @@\n-a\n+b\n";

    [Theory]
    [InlineData(1_000_000L, 1_000_001L, "pass")]
    [InlineData(1_000_000L, 999_999L, "fail")]
    [InlineData(null, 1L, "error")]
    public void MaxDurationUsesTheManifestWallTime(long? wallTime, long spec, string expected)
    {
        var dir = Directory.CreateTempSubdirectory("proctor-cell").FullName;
        if (wallTime is not null) File.WriteAllText(Path.Combine(dir, Layout.ManifestFile), $"{{\"duration_ms\": {wallTime}}}");
        var v = Checks.Evaluate(JsonNode.Parse($"{{\"max_duration_ms\": {spec}}}")!.AsObject(), Cell(cellDir: dir), Fixture("plain"));
        Assert.True(expected == v.Result, $"{v.Result} ({v.Reason})");
    }

    [Theory]
    // check spec, fixture, expected verdict
    [InlineData("{\"exit_reason\": \"ok\"}", "plain", "pass")]
    [InlineData("{\"exit_reason\": \"ok\"}", "loop-nudged", "fail")]
    [InlineData("{\"answer_contains\": \"forty-two\"}", "plain", "pass")]
    [InlineData("{\"answer_contains\": \"forty-three\"}", "plain", "fail")]
    [InlineData("{\"answer_contains\": \"x\"}", "provider-error", "error")]
    [InlineData("{\"answer_regex\": \"^The answer is [a-z-]+\\\\.$\"}", "plain", "pass")]
    [InlineData("{\"answer_regex\": \"^\\\\d+$\"}", "plain", "fail")]
    [InlineData("{\"answer_equals\": \"The answer is forty-two.\"}", "plain", "pass")]
    [InlineData("{\"answer_equals\": \"The answer is 42.\"}", "plain", "fail")]
    [InlineData("{\"answer_words\": {\"min\": 3, \"max\": 5}}", "plain", "pass")]
    [InlineData("{\"answer_words\": {\"max\": 3}}", "plain", "fail")]
    [InlineData("{\"tools_used\": [\"bash\"]}", "tool-allowed", "pass")]
    [InlineData("{\"tools_used\": [\"bash\", \"read_file\"]}", "tool-allowed", "fail")]
    [InlineData("{\"tools_used_any\": [\"read_file\", \"bash\"]}", "tool-allowed", "pass")]
    [InlineData("{\"tools_used_any\": [\"read_file\", \"grep\"]}", "tool-allowed", "fail")]
    [InlineData("{\"tool_args\": {\"name\": \"bash\", \"args\": {\"command\": \"echo hi\"}}}", "tool-allowed", "pass")]
    [InlineData("{\"tool_args\": {\"name\": \"bash\", \"args\": {\"command\": \"echo hi\"}, \"mode\": \"exact\"}}", "tool-allowed", "fail")]
    [InlineData("{\"tool_args\": {\"name\": \"bash\", \"args\": {\"command\": \"echo hi\"}, \"mode\": \"exact\", \"ignore\": [\"desc*\"]}}", "tool-allowed", "pass")]
    [InlineData("{\"tool_args\": {\"name\": \"bash\", \"args\": {\"command\": \"echo bye\"}}}", "tool-allowed", "fail")]
    [InlineData("{\"tool_args\": {\"name\": \"grep\", \"args\": {}}}", "tool-allowed", "fail")]
    [InlineData("{\"tool_sequence\": {\"names\": [\"bash\", \"bash\"], \"mode\": \"in_order\"}}", "loop-nudged", "pass")]
    [InlineData("{\"tool_sequence\": {\"names\": [\"bash\", \"bash\"], \"mode\": \"exact\"}}", "loop-nudged", "fail")]
    [InlineData("{\"tool_sequence\": {\"names\": [\"bash\", \"bash\", \"bash\"], \"mode\": \"exact\"}}", "loop-nudged", "pass")]
    [InlineData("{\"denied_calls\": {\"max\": 0}}", "tool-allowed", "pass")]
    [InlineData("{\"denied_calls\": {\"max\": 0}}", "tool-denied", "fail")]
    [InlineData("{\"tool_errors\": {\"max\": 0}}", "tool-allowed", "pass")]
    [InlineData("{\"tool_errors\": {\"max\": 0}}", "tool-error", "fail")]
    [InlineData("{\"loop_nudged\": false}", "plain", "pass")]
    [InlineData("{\"loop_nudged\": false}", "loop-nudged", "fail")]
    [InlineData("{\"max_tool_calls\": 3}", "loop-nudged", "pass")]
    [InlineData("{\"max_tool_calls\": 2}", "loop-nudged", "fail")]
    [InlineData("{\"max_tokens\": 100}", "plain", "pass")]
    [InlineData("{\"max_tokens\": 10}", "plain", "fail")]
    [InlineData("{\"max_tokens\": 10}", "provider-error", "error")]
    public void BuiltIns(string spec, string fixture, string expected)
    {
        var verdict = Eval(spec, Fixture(fixture));
        Assert.True(expected == verdict.Result, $"{spec} on {fixture}: {verdict.Result} ({verdict.Reason})");
        Assert.NotEmpty(verdict.Reason);
    }

    [Theory]
    [InlineData("[\"src/fetch.cs\"]", "at_least", "pass")]
    [InlineData("[\"src/*.cs\", \"README.md\"]", "exactly", "pass")]
    [InlineData("[\"src/fetch.cs\"]", "exactly", "fail")]
    [InlineData("[\"src/**\", \"README.md\", \"docs/*\"]", "at_most", "pass")]
    [InlineData("[\"src/**\"]", "at_most", "fail")]
    [InlineData("[\"tests/x.cs\"]", "at_least", "fail")]
    public void FilesTouched(string paths, string mode, string expected)
    {
        var verdict = Eval($"{{\"files_touched\": {{\"paths\": {paths}, \"mode\": \"{mode}\"}}}}", Fixture("plain", Diff));
        Assert.True(expected == verdict.Result, $"{paths} {mode}: {verdict.Result} ({verdict.Reason})");
    }

    [Fact]
    public void FilesTouched_WithoutADiffIsAnError() =>
        Assert.Equal("error", Eval("{\"files_touched\": {\"paths\": [\"x\"], \"mode\": \"at_least\"}}", Fixture("plain")).Result);

    [Fact]
    public void Negation_FlipsPassAndFail_ButNotError()
    {
        Assert.Equal("pass", Eval("{\"not_answer_contains\": \"forty-three\"}", Fixture("plain")).Result);
        var v = Eval("{\"not_answer_contains\": \"forty-two\"}", Fixture("plain"));
        Assert.Equal("fail", v.Result);
        Assert.StartsWith("not: ", v.Reason);
        Assert.Equal("error", Eval("{\"not_answer_contains\": \"x\"}", Fixture("provider-error")).Result);
    }

    [Fact]
    public void Conjunction_WorstVerdictWins_ReasonsJoined()
    {
        var v = Eval("{\"max_tool_calls\": 40, \"exit_reason\": \"ok\"}", Fixture("loop-nudged"));
        Assert.Equal("fail", v.Result);
        Assert.Contains("3 tool calls", v.Reason);
        Assert.Contains("exit_reason=max_tool_calls", v.Reason);
        Assert.Equal("error", Eval("{\"exit_reason\": \"ok\", \"answer_contains\": \"x\"}", Fixture("provider-error")).Result);
    }

    [Fact]
    public void FromExpect_ReadsTheCaseBlock_AndErrorsWhenAbsent()
    {
        var expect = JsonNode.Parse("{\"answer_contains\": \"forty\"}")!.AsObject();
        Assert.Equal("pass", Eval("{\"answer_contains\": \"@expect\"}", Fixture("plain"), expect).Result);
        var v = Eval("{\"tools_used\": \"@expect\"}", Fixture("plain"), expect);
        Assert.Equal("error", v.Result);
        Assert.Contains("expect.tools_used", v.Reason);
    }

    [Theory]
    [InlineData("echo 'built fine'; exit 0", "pass", "built fine")]
    [InlineData("echo '2 of 41 tests failed'; exit 1", "fail", "2 of 41 tests failed")]
    [InlineData("echo 'touched 1 file outside scope'; exit 2", "needs-judge", "touched 1 file outside scope")]
    [InlineData("exit 0", "pass", "pass")]
    [InlineData("echo boom >&2; exit 9", "error", "exited 9")]
    public void Script_ExitCodeIsTheVerdict_FirstStdoutLineIsTheReason(string body, string expected, string reasonContains)
    {
        using var repo = new TestRepo();
        var script = repo.Write("e/checks/c.sh", "#!/usr/bin/env bash\n" + body + "\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var cell = Cell(evalDir: Path.Combine(repo.Root, "evals", "e"), cellDir: repo.Root);

        var v = Checks.Evaluate(JsonNode.Parse("{\"script\": \"checks/c.sh\"}")!.AsObject(), cell, Fixture("plain"));
        Assert.Equal(expected, v.Result);
        Assert.Contains(reasonContains, v.Reason);
    }

    [Fact]
    public void Script_ThatCannotRunIsAnError_NotAFail()
    {
        using var repo = new TestRepo();
        repo.Write("e/checks/noexec.sh", "#!/usr/bin/env bash\nexit 0\n");   // not executable
        var cell = Cell(evalDir: Path.Combine(repo.Root, "evals", "e"), cellDir: repo.Root);
        var v = Checks.Evaluate(JsonNode.Parse("{\"script\": \"checks/noexec.sh\"}")!.AsObject(), cell, Fixture("plain"));
        Assert.Equal("error", v.Result);
        Assert.Contains("could not run", v.Reason);
    }

    [Fact]
    public void Script_SeesTheCellInItsEnvironment()
    {
        using var repo = new TestRepo();
        var script = repo.Write("e/checks/env.sh", "#!/usr/bin/env bash\n[ \"$PROCTOR_CASE\" = c ] && [ \"$PROCTOR_ARM\" = a ] && [ \"$(pwd)\" = \"$PROCTOR_CELL\" ] && echo \"$PROCTOR_EXPECT\" | grep -q forty && exit 0\nexit 1\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var cell = Cell(JsonNode.Parse("{\"answer_contains\": \"forty\"}")!.AsObject(), Path.Combine(repo.Root, "evals", "e"), repo.Root);
        Assert.Equal("pass", Checks.Evaluate(JsonNode.Parse("{\"script\": \"checks/env.sh\"}")!.AsObject(), cell, Fixture("plain")).Result);
    }

    [Theory]
    [InlineData("src/*.cs", "src/a.cs", true)]
    [InlineData("src/*.cs", "src/sub/a.cs", false)]
    [InlineData("src/**", "src/sub/a.cs", true)]
    [InlineData("**/*.cs", "a.cs", true)]
    [InlineData("**/*.cs", "x/y/a.cs", true)]
    [InlineData("README.md", "README.md", true)]
    [InlineData("README.md", "docs/README.md", false)]
    public void Globs(string pattern, string path, bool expected) => Assert.Equal(expected, Glob.IsMatch(pattern, path));
}
