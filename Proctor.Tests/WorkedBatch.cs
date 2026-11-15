using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

/// <summary>The work batch's shape from project/plans/first-slice.md as fixed data: two variants, three tasks, three attempts.</summary>
static class WorkedBatch
{
    public static Exam Exam(int attempts = 3, bool twoVariants = true)
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.EditJson("smoke/exam.json", e =>
        {
            e["description"] = "Can a local coder model make a small change to a .NET repository so that it builds and the tests pass?";
            e["variants"]![0]!["description"] = "the local floor";
            e["variants"]![0]!["id"] = "floor"; e["variants"]![0]!["attempts"] = attempts; e["variants"]![0]!["model"] = "qwen-coder"; e["variants"]![0]!["provider"] = "local-qcoder";
            e["variants"]![1]!["id"] = "b"; e["variants"]![1]!["attempts"] = attempts; e["variants"]![1]!["model"] = "glm"; e["variants"]![1]!["provider"] = "local-glm";
            if (!twoVariants) e["variants"]!.AsArray().RemoveAt(1);
            e["grading"]!["checks"] = JsonNode.Parse("{\"exit_ok\": {\"exit_reason\": \"ok\"}, \"builds\": {\"script\": \"checks/answer-nonempty.sh\", \"description\": \"the repository builds after the change\"}, \"no_denials\": {\"denied_calls\": {\"max\": 0}}}");
            e["grading"]!["pass"] = JsonNode.Parse("[\"exit_ok\", \"builds\"]");
            e["grading"]!["validity"] = JsonNode.Parse("[\"no_denials\"]");
        });
        repo.EditJson("smoke/tasks/uses-bash.json", t => t["checks"] = JsonNode.Parse("{\"used-bash\": {\"tools_used\": [\"bash\"]}}"));
        return repo.LoadExam("smoke");
    }

    public static Batch Batch(Exam exam) => new(
        "20260917-1432-smoke-k7px", exam.Id, "sha256:9c1e0000", System.Text.Json.JsonSerializer.SerializeToNode(exam.Def, Proctor.Exam.JsonOptions)!.AsObject(),
        exam.Tasks.Select(c => c.Id!).ToList(), exam.PlannedAttempts, "2026-09-17T14:32:00Z", "proctor run smoke", "bench",
        new() { ["proctor"] = "0.1.0", ["nb"] = "1.0.0" }, new GitInfo("3f2c1e9a", Dirty: false), new ResolvedNb("/usr/local/bin/nb", null));

    /// <summary>Last week's floor, pinned: loops was perfect, plain was perfect, uses-bash was one in three.</summary>
    public static GuardInput Guard() =>
        new("2026-09-14T09:12:00Z", "as pinned", new() { ["loops"] = 1.0, ["plain"] = 1.0, ["uses-bash"] = 0.333 }, TolerancePoints: 10);

    public static ResultRow Row(string variant, string task, int n, bool? pass, string status = "completed", string exit = "ok", long tokens = 57331, long durationMs = 1118000, string? reason = null, string? invalid = null) =>
        new(Id: $"{variant[0]}{@task[0]}{n}0000000000000", Variant: variant, Task: task, Attempt: n, Status: status,
            StatusReason: pass is null && status != "completed" ? (reason ?? "attempt setup hook failed: hooks/reset-fixture.sh exited 1: clone failed") : null,
            ExitReason: status == "completed" ? exit : null,
            Usage: status == "completed" ? new UsageRow(tokens - 9120, 9120, tokens, false) : null,
            ToolCalls: status == "completed" ? 23 : null, DeniedCalls: status == "completed" ? 0 : null, DurationMs: durationMs,
            Checks: pass is null ? null : Checks(task, exit, pass, invalid),
            Pass: pass,
            Reasons: pass == false ? new() { [exit == "ok" ? "builds" : "exit_ok"] = exit == "ok" ? "2 of 41 tests failed" : $"exit_reason={exit}" } : null,
            Invalid: invalid);

    static Dictionary<string, string> Checks(string task, string exit, bool? pass, string? invalid)
    {
        var checks = new Dictionary<string, string> { ["exit_ok"] = exit == "ok" ? "pass" : "fail", ["builds"] = pass == true ? "pass" : "fail", ["no_denials"] = invalid is null ? "pass" : "fail" };
        if (task == "uses-bash") checks["used-bash"] = "pass";   // the task's own check, declared in its file
        return checks;
    }

    /// <summary>Nine attempts per variant; the floor loses one attempt to a hook failure and one to a validity check, so the warning band and both kinds of exclusion render.</summary>
    public static List<ResultRow> Rows()
    {
        var rows = new List<ResultRow>();
        var floor = new Dictionary<string, bool?[]> { ["loops"] = [true, true, false], ["plain"] = [true, true, true], ["uses-bash"] = [false, null, false] };
        var b = new Dictionary<string, bool?[]> { ["loops"] = [true, true, true], ["plain"] = [true, true, true], ["uses-bash"] = [true, false, true] };
        foreach (var (c, v) in floor) for (var s = 0; s < 3; s++)
            rows.Add(Row("floor", c, s + 1, v[s], status: v[s] is null ? "failed" : "completed", exit: v[s] == false && c == "loops" ? "max_tool_calls" : "ok", tokens: 41200 + 3000 * s + (c == "plain" ? 20000 : 0), durationMs: 812000 + 100000 * s + (c == "loops" ? 400000 : 0),
                invalid: c == "plain" && s == 2 ? "no_denials: 1 denied call: bash (no-match)" : null));
        foreach (var (c, v) in b) for (var s = 0; s < 3; s++)
            rows.Add(Row("b", c, s + 1, v[s], tokens: 61000 + 2000 * s, durationMs: 990000 + 50000 * s));
        return rows;
    }
}
