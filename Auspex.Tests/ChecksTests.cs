using System.Text.Json.Nodes;
using Auspex;

namespace Auspex.Tests;

/// <summary>Step 5: every built-in has a passing and a failing task; negation; @expect; the script contract.</summary>
public class ChecksTests
{
    static Transcript Fixture(string name, string? diff = null) =>
        Transcript.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name + ".jsonl")), diff);

    static AttemptContext Attempt(JsonObject? expect = null, string? examDir = null, string? attemptDir = null) =>
        new(examDir ?? "/nonexistent", attemptDir ?? Path.GetTempPath(), "/work", "exp", "a", new StageDef("c", null, "p", expect), 1);

    static Verdict Exam(string spec, Transcript t, JsonObject? expect = null) =>
        Checks.Evaluate(JsonNode.Parse(spec)!.AsObject(), Attempt(expect), t);

    const string Diff = "diff --git a/src/fetch.cs b/src/fetch.cs\n--- a/src/fetch.cs\n+++ b/src/fetch.cs\n@@ -1 +1 @@\n-a\n+b\ndiff --git a/README.md b/README.md\n--- a/README.md\n+++ b/README.md\n@@ -1 +1 @@\n-a\n+b\n";

    [Theory]
    [InlineData(1_000_000L, 1_000_001L, "pass")]
    [InlineData(1_000_000L, 999_999L, "fail")]
    [InlineData(null, 1L, "error")]
    public void MaxDurationUsesTheManifestWallTime(long? wallTime, long spec, string expected)
    {
        var dir = Directory.CreateTempSubdirectory("auspex-attempt").FullName;
        if (wallTime is not null) File.WriteAllText(Path.Combine(dir, Layout.ManifestFile), $"{{\"duration_ms\": {wallTime}}}");
        var v = Checks.Evaluate(JsonNode.Parse($"{{\"max_duration_ms\": {spec}}}")!.AsObject(), Attempt(attemptDir: dir), Fixture("plain"));
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
    [InlineData("{\"tool_args\": {\"name\": \"bash\", \"args\": {\"command\": \"^echo \"}, \"match\": \"regex\"}}", "tool-allowed", "pass")]
    [InlineData("{\"tool_args\": {\"name\": \"bash\", \"args\": {\"command\": \"^echo$\"}, \"match\": \"regex\"}}", "tool-allowed", "fail")]
    [InlineData("{\"tool_args\": {\"name\": \"bash\", \"args\": {\"command\": \"^echo \"}}}", "tool-allowed", "fail")]
    [InlineData("{\"not_tool_args\": {\"name\": \"bash\", \"args\": {\"command\": \"^rm \"}, \"match\": \"regex\"}}", "tool-allowed", "pass")]
    [InlineData("{\"tool_sequence\": {\"names\": [\"bash\", \"bash\"], \"mode\": \"in_order\"}}", "loop-nudged", "pass")]
    [InlineData("{\"tool_sequence\": {\"names\": [\"bash\", \"bash\"], \"mode\": \"exact\"}}", "loop-nudged", "fail")]
    [InlineData("{\"tool_sequence\": {\"names\": [\"bash\", \"bash\", \"bash\"], \"mode\": \"exact\"}}", "loop-nudged", "pass")]
    [InlineData("{\"denied_calls\": {\"max\": 0}}", "tool-allowed", "pass")]
    [InlineData("{\"denied_calls\": {\"max\": 0}}", "tool-denied", "fail")]
    [InlineData("{\"tool_errors\": {\"max\": 0}}", "tool-allowed", "pass")]
    [InlineData("{\"tool_errors\": {\"max\": 0}}", "tool-error", "fail")]
    [InlineData("{\"result_matches\": \"exit code: [1-9]\"}", "tool-error", "pass")]
    [InlineData("{\"result_matches\": \"exit code: [1-9]\"}", "tool-allowed", "fail")]
    [InlineData("{\"not_result_matches\": \"exit code: [1-9]\"}", "tool-error", "fail")]
    [InlineData("{\"loop_nudged\": false}", "plain", "pass")]
    [InlineData("{\"loop_nudged\": false}", "loop-nudged", "fail")]
    [InlineData("{\"max_tool_calls\": 3}", "loop-nudged", "pass")]
    [InlineData("{\"max_tool_calls\": 2}", "loop-nudged", "fail")]
    [InlineData("{\"max_tokens\": 100}", "plain", "pass")]
    [InlineData("{\"max_tokens\": 10}", "plain", "fail")]
    [InlineData("{\"max_tokens\": 10}", "provider-error", "error")]
    public void BuiltIns(string spec, string fixture, string expected)
    {
        var verdict = Exam(spec, Fixture(fixture));
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
        var verdict = Exam($"{{\"files_touched\": {{\"paths\": {paths}, \"mode\": \"{mode}\"}}}}", Fixture("plain", Diff));
        Assert.True(expected == verdict.Result, $"{paths} {mode}: {verdict.Result} ({verdict.Reason})");
    }

    [Fact]
    public void FilesTouched_WithoutADiffIsAnError() =>
        Assert.Equal("error", Exam("{\"files_touched\": {\"paths\": [\"x\"], \"mode\": \"at_least\"}}", Fixture("plain")).Result);

    [Theory]
    [InlineData("{\"max_duration_ms\": 1800000}", "finishes within 30 min")]
    [InlineData("{\"max_duration_ms\": 1500}", "finishes within 1500 ms")]
    [InlineData("{\"not_exit_reason\": \"ok\"}", "not: the attempt ends with 'ok'")]
    [InlineData("{\"files_touched\": \"@expect\"}", "the files changed are the ones the stage expects")]
    [InlineData("{\"files_touched\": {\"paths\": [\"src/**\", \"README.md\"], \"mode\": \"at_most\"}}", "changes stay within src/**, README.md")]
    [InlineData("{\"tool_sequence\": {\"names\": [\"read\", \"bash\"], \"mode\": \"exact\"}}", "calls read, bash and nothing else")]
    [InlineData("{\"answer_words\": {\"min\": 10, \"max\": 50}}", "the answer is 10 to 50 words")]
    [InlineData("{\"not_result_matches\": \"ask the user to run\"}", "not: a tool result matches /ask the user to run/")]
    [InlineData("{\"loop_nudged\": false, \"tool_errors\": {\"max\": 2}}", "the loop nudge does not fire and at most 2 tool errors")]
    [InlineData("{\"exit_reason\": \"ok\", \"description\": \"nb finished cleanly\"}", "nb finished cleanly")]
    public void Describe_IsTheDeclaredSentence_OrDerivedFromTheSpec(string spec, string expected) =>
        Assert.Equal(expected, Checks.Describe(JsonNode.Parse(spec)!.AsObject()));

    [Fact]
    public void ResultMatches_ReadsAnAgentAttemptsToolResults()
    {
        var trajectory = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "atif-claude-code.json")))!.AsObject();
        var t = Transcript.FromAtif(trajectory, null);

        var shown = Exam("{\"result_matches\": \"has been updated\\\\s+successfully\"}", t);
        var never = Exam("{\"not_result_matches\": \"ask the user to run\"}", t);

        Assert.Equal("pass", shown.Result);
        Assert.Contains("has been updated successfully", shown.Reason);
        Assert.Equal("pass", never.Result);
    }

    [Theory]
    [InlineData("{\"exit_reason\": \"ok\"}", "An attempt that ended on a limit")]
    [InlineData("{\"denied_calls\": {\"max\": 0}}", "A denied call means")]
    [InlineData("{\"loop_nudged\": false}", "A nudge means")]
    [InlineData("{\"max_tool_calls\": 60, \"max_duration_ms\": 1200000}", "An attempt over its budget")]
    [InlineData("{\"max_tokens\": 5}", "An attempt over its budget")]
    [InlineData("{\"exit_reason\": \"oracle_budget\"}", null)]
    [InlineData("{\"loop_nudged\": true}", null)]
    [InlineData("{\"exit_reason\": \"ok\", \"max_tokens\": 5}", null)]
    [InlineData("{\"answer_contains\": \"x\"}", null)]
    public void DefaultWhy_CoversTheGuardrails_ByWhatTheyCheck(string spec, string? starts)
    {
        var why = Checks.DefaultWhy(JsonNode.Parse(spec)!.AsObject());
        if (starts is null) Assert.Null(why); else Assert.StartsWith(starts, why);
    }

    [Fact]
    public void Why_TheAuthorsSentenceWins_AndIsNeitherEvaluatedNorEmpty()
    {
        var spec = JsonNode.Parse("{\"exit_reason\": \"ok\", \"why\": \"a crash is the harness's fault, not the agent's\"}")!.AsObject();
        Assert.Equal("a crash is the harness's fault, not the agent's", Checks.WhyOf(spec));
        var v = Exam(spec.ToJsonString(), Fixture("plain"));
        Assert.Equal(("pass", "exit_reason=ok"), (v.Result, v.Reason));
        Assert.Contains(Checks.ValidateCheck(JsonNode.Parse("{\"exit_reason\": \"ok\", \"why\": \" \"}")!.AsObject(), "/nonexistent"), p => p.Field == "why");
        Assert.Contains(Checks.ValidateCheck(JsonNode.Parse("{\"why\": \"only a reason\"}")!.AsObject(), "/nonexistent"), p => p.Field == "");
    }

    static Transcript Trajectory(string name, string? diff = null) =>
        Transcript.FromAtif(JsonNode.Parse(File.ReadAllText(Path.Combine(TestRepo.SourceRoot, "Auspex.Tests", "fixtures", "nb-run", name + ".trajectory.json")))!.AsObject(), null, diff);

    static List<string?> Steps(Verdict v) => [.. (v.Evidence ?? []).Select(e => e.Step)];

    [Fact]
    public void Evidence_PointsAtTheStepsWhereTheFieldFoundWhatItLooksFor()
    {
        var t = Trajectory("loop-nudged");
        Assert.Equal(["2", "3", "4", "6"], Steps(Exam("{\"tools_used\": [\"bash\"]}", t)));
        Assert.Equal(["2", "3", "4", "6"], Steps(Exam("{\"tool_args\": {\"name\": \"bash\", \"args\": {}}}", t)));
        Assert.Equal(["7"], Steps(Exam("{\"answer_contains\": \"maximum\"}", t)));
        // A negated field that fails points at what it found; a field that found nothing points at nothing.
        var never = Exam("{\"not_tools_used\": [\"bash\"]}", t);
        Assert.Equal("fail", never.Result);
        Assert.Equal(["2", "3", "4", "6"], Steps(never));
        Assert.Null(Exam("{\"tools_used\": [\"read_file\"]}", t).Evidence);
        // A check on the run as a whole has none.
        Assert.Null(Exam("{\"exit_reason\": \"ok\"}", t).Evidence);
        Assert.Null(Exam("{\"loop_nudged\": true}", t).Evidence);
    }

    [Fact]
    public void Evidence_ForFiles_IsTheTouchedPathsTheModeIsAbout()
    {
        var t = Trajectory("plain", Diff);
        var least = Exam("{\"files_touched\": {\"paths\": [\"src/**\"], \"mode\": \"at_least\"}}", t);
        Assert.Equal(["src/fetch.cs"], (least.Evidence ?? []).Select(e => e.File));
        var most = Exam("{\"files_touched\": {\"paths\": [\"src/**\"], \"mode\": \"at_most\"}}", t);
        Assert.Equal("fail", most.Result);
        Assert.Equal(["README.md"], (most.Evidence ?? []).Select(e => e.File));
    }

    [Fact]
    public void Evidence_IsEveryPartsInAConjunction_AndNoneFromAJsonlTranscript()
    {
        var both = Exam("{\"tools_used\": [\"bash\"], \"answer_contains\": \"maximum\"}", Trajectory("loop-nudged"));
        Assert.Equal(["2", "3", "4", "6", "7"], Steps(both));
        Assert.Null(Exam("{\"tools_used\": [\"bash\"]}", Fixture("loop-nudged")).Evidence);
    }

    [Fact]
    public void Description_IsNotEvaluated()
    {
        var v = Exam("{\"exit_reason\": \"ok\", \"description\": \"nb finished cleanly\"}", Fixture("plain"));
        Assert.Equal("pass", v.Result);
        Assert.Equal("exit_reason=ok", v.Reason);
    }

    [Fact]
    public void Negation_FlipsPassAndFail_ButNotError()
    {
        Assert.Equal("pass", Exam("{\"not_answer_contains\": \"forty-three\"}", Fixture("plain")).Result);
        var v = Exam("{\"not_answer_contains\": \"forty-two\"}", Fixture("plain"));
        Assert.Equal("fail", v.Result);
        Assert.StartsWith("not: ", v.Reason);
        Assert.Equal("error", Exam("{\"not_answer_contains\": \"x\"}", Fixture("provider-error")).Result);
    }

    [Fact]
    public void Conjunction_WorstVerdictWins_ReasonsJoined()
    {
        var v = Exam("{\"max_tool_calls\": 40, \"exit_reason\": \"ok\"}", Fixture("loop-nudged"));
        Assert.Equal("fail", v.Result);
        Assert.Contains("3 tool calls", v.Reason);
        Assert.Contains("exit_reason=max_tool_calls", v.Reason);
        Assert.Equal("error", Exam("{\"exit_reason\": \"ok\", \"answer_contains\": \"x\"}", Fixture("provider-error")).Result);
    }

    [Fact]
    public void FromExpect_ReadsTheTaskBlock_AndErrorsWhenAbsent()
    {
        var expect = JsonNode.Parse("{\"answer_contains\": \"forty\"}")!.AsObject();
        Assert.Equal("pass", Exam("{\"answer_contains\": \"@expect\"}", Fixture("plain"), expect).Result);
        var v = Exam("{\"tools_used\": \"@expect\"}", Fixture("plain"), expect);
        Assert.Equal("error", v.Result);
        Assert.Contains("expect.tools_used", v.Reason);
    }

    [Fact]
    public void FromExpect_IsKeyedByTheCheckName_BeforeTheField()
    {
        var t = Fixture("plain");
        var expect = JsonNode.Parse("{\"greets\": {\"answer_contains\": \"zzz\"}, \"answer_contains\": \"forty\"}")!.AsObject();
        var spec = JsonNode.Parse("{\"answer_contains\": \"@expect\"}")!.AsObject();
        Assert.Equal("fail", Checks.Evaluate(spec, Attempt(expect), t, "greets").Result);
        Assert.Equal("pass", Checks.Evaluate(spec, Attempt(expect), t, "other").Result);
        var v = Checks.Evaluate(JsonNode.Parse("{\"tools_used\": \"@expect\"}")!.AsObject(), Attempt(expect), t, "uses");
        Assert.Equal(("error", "tools_used: stage has no expect.uses.tools_used or expect.tools_used"), (v.Result, v.Reason));
    }

    [Fact]
    public void Combined_ReasonLeadsWithThePartsThatDecidedIt()
    {
        var v = Exam("{\"answer_contains\": \"forty-two\", \"answer_words\": {\"max\": 3}}", Fixture("plain"));
        Assert.Equal("fail", v.Result);
        Assert.DoesNotContain("forty-two", v.Reason.Split("; ")[0]);
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
        var attempt = Attempt(examDir: Path.Combine(repo.Root, "exams", "e"), attemptDir: repo.Root);

        var v = Checks.Evaluate(JsonNode.Parse("{\"script\": \"checks/c.sh\"}")!.AsObject(), attempt, Fixture("plain"));
        Assert.Equal(expected, v.Result);
        Assert.Contains(reasonContains, v.Reason);
    }

    [Fact]
    public void Script_ThatCannotRunIsAnError_NotAFail()
    {
        using var repo = new TestRepo();
        repo.Write("e/checks/noexec.sh", "#!/usr/bin/env bash\nexit 0\n");   // not executable
        var attempt = Attempt(examDir: Path.Combine(repo.Root, "exams", "e"), attemptDir: repo.Root);
        var v = Checks.Evaluate(JsonNode.Parse("{\"script\": \"checks/noexec.sh\"}")!.AsObject(), attempt, Fixture("plain"));
        Assert.Equal("error", v.Result);
        Assert.Contains("could not run", v.Reason);
    }

    [Fact]
    public void Script_SeesTheAttemptInItsEnvironment()
    {
        using var repo = new TestRepo();
        var script = repo.Write("e/checks/env.sh", "#!/usr/bin/env bash\n[ \"$AUSPEX_STAGE\" = c ] && [ \"$AUSPEX_CONFIGURATION\" = a ] && [ \"$(pwd)\" = \"$AUSPEX_ATTEMPT_DIR\" ] && echo \"$AUSPEX_EXPECT\" | grep -q forty && exit 0\nexit 1\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var attempt = Attempt(JsonNode.Parse("{\"answer_contains\": \"forty\"}")!.AsObject(), Path.Combine(repo.Root, "exams", "e"), repo.Root);
        Assert.Equal("pass", Checks.Evaluate(JsonNode.Parse("{\"script\": \"checks/env.sh\"}")!.AsObject(), attempt, Fixture("plain")).Result);
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

    [Theory]
    [InlineData("{\"name\": \"web_fetch\", \"args\": {\"url\": \"^http://\"}, \"match\": \"regex\"}", null)]
    [InlineData("{\"name\": \"web_fetch\", \"args\": {\"url\": \"(\"}, \"match\": \"regex\"}", "args.url is not a valid regular expression")]
    [InlineData("{\"name\": \"web_fetch\", \"args\": {}, \"match\": \"glob\"}", "match is equals or regex")]
    public void ToolArgs_MatchIsValidated(string spec, string? problem) =>
        Assert.Equal(problem, Checks.ValidateSpec("tool_args", JsonNode.Parse(spec), "/nonexistent"));

    [Fact]
    public void ToolArgs_ABadPatternFromTheTask_IsAnError()
    {
        var v = Exam("{\"tool_args\": \"@expect\"}", Fixture("tool-allowed"),
            JsonNode.Parse("{\"tool_args\": {\"name\": \"bash\", \"args\": {\"command\": \"(\"}, \"match\": \"regex\"}}")!.AsObject());
        Assert.Equal(Verdict.Error, v.Result);
    }
}
