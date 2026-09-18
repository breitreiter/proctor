using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

/// <summary>The work experiment's shape from project/plans/first-slice.md as fixed data: two arms, three cases, three samples.</summary>
static class WorkedExperiment
{
    public static Eval Eval(int samples = 3, bool twoArms = true)
    {
        using var repo = new TestRepo();
        repo.CopyEval("smoke");
        repo.EditJson("smoke/eval.json", e =>
        {
            e["arms"]![0]!["id"] = "floor"; e["arms"]![0]!["samples"] = samples; e["arms"]![0]!["model"] = "qwen-coder"; e["arms"]![0]!["provider"] = "imp-qcoder";
            e["arms"]![1]!["id"] = "b"; e["arms"]![1]!["samples"] = samples; e["arms"]![1]!["model"] = "glm"; e["arms"]![1]!["provider"] = "imp-glm";
            if (!twoArms) e["arms"]!.AsArray().RemoveAt(1);
            e["grading"]!["checks"] = JsonNode.Parse("{\"exit_ok\": {\"exit_reason\": \"ok\"}, \"builds\": {\"script\": \"checks/answer-nonempty.sh\"}, \"no_denials\": {\"denied_calls\": {\"max\": 0}}}");
            e["grading"]!["pass"] = JsonNode.Parse("[\"exit_ok\", \"builds\"]");
        });
        return repo.LoadEval("smoke");
    }

    public static Experiment Experiment(Eval eval) => new(
        "20260917-1432-smoke-k7px", eval.Id, "sha256:9c1e0000", System.Text.Json.JsonSerializer.SerializeToNode(eval.Def, Proctor.Eval.JsonOptions)!.AsObject(),
        eval.Cases.Select(c => c.Id!).ToList(), eval.PlannedCells, "2026-09-17T14:32:00Z", "proctor run smoke", "imp",
        new() { ["proctor"] = "0.1.0", ["nb"] = "1.0.0" }, new GitInfo("3f2c1e9a", Dirty: false), new ResolvedNb("/usr/local/bin/nb", null));

    public static ResultRow Row(string arm, string @case, int sample, bool? pass, string status = "completed", string exit = "ok", long tokens = 57331, long durationMs = 1118000, string? reason = null) =>
        new(RunId: $"{arm[0]}{@case[0]}{sample}0000000000000", Arm: arm, Case: @case, Sample: sample, Status: status,
            StatusReason: pass is null && status != "completed" ? (reason ?? "sample setup hook failed: hooks/reset-fixture.sh exited 1: clone failed") : null,
            ExitReason: status == "completed" ? exit : null,
            Usage: status == "completed" ? new UsageRow(tokens - 9120, 9120, tokens, false) : null,
            ToolCalls: status == "completed" ? 23 : null, DeniedCalls: status == "completed" ? 0 : null, DurationMs: durationMs,
            Checks: pass is null ? null : new() { ["exit_ok"] = exit == "ok" ? "pass" : "fail", ["builds"] = pass == true ? "pass" : "fail", ["no_denials"] = "pass" },
            Pass: pass,
            Reasons: pass == false ? new() { [exit == "ok" ? "builds" : "exit_ok"] = exit == "ok" ? "2 of 41 tests failed" : $"exit_reason={exit}" } : null);

    /// <summary>Nine cells per arm; the floor loses one cell to a hook failure so the warning band and exclusions render.</summary>
    public static List<ResultRow> Rows()
    {
        var rows = new List<ResultRow>();
        var floor = new Dictionary<string, bool?[]> { ["loops"] = [true, true, false], ["plain"] = [true, true, true], ["uses-bash"] = [false, null, false] };
        var b = new Dictionary<string, bool?[]> { ["loops"] = [true, true, true], ["plain"] = [true, true, true], ["uses-bash"] = [true, false, true] };
        foreach (var (c, v) in floor) for (var s = 0; s < 3; s++)
            rows.Add(Row("floor", c, s + 1, v[s], status: v[s] is null ? "failed" : "completed", exit: v[s] == false && c == "loops" ? "max_tool_calls" : "ok", tokens: 41200 + 3000 * s + (c == "plain" ? 20000 : 0), durationMs: 812000 + 100000 * s + (c == "loops" ? 400000 : 0)));
        foreach (var (c, v) in b) for (var s = 0; s < 3; s++)
            rows.Add(Row("b", c, s + 1, v[s], tokens: 61000 + 2000 * s, durationMs: 990000 + 50000 * s));
        return rows;
    }
}
