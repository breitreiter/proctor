using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

/// <summary>The work batch's shape from project/archive/first-slice.md as fixed data: two variants, three tasks, three attempts.</summary>
static class WorkedBatch
{
    /// <summary>The worked exam: the floor and b, or with <paramref name="third"/> also c, a copy of b; with <paramref name="twoVariants"/> false, the floor alone.</summary>
    public static Exam Exam(int attempts = 3, bool twoVariants = true, bool third = false)
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.EditJson("smoke/exam.json", e =>
        {
            e["description"] = "Can a local coder model make a small change to a .NET repository so that it builds and the tests pass?";
            e["attempts"] = attempts;
            e["grading"]!["checks"] = JsonNode.Parse("{\"exit_ok\": {\"exit_reason\": \"ok\"}, \"builds\": {\"script\": \"checks/answer-nonempty.sh\", \"description\": \"the repository builds after the change\"}, \"no_denials\": {\"denied_calls\": {\"max\": 0}}}");
            e["grading"]!["pass"] = JsonNode.Parse("[\"exit_ok\", \"builds\"]");
            e["grading"]!["validity"] = JsonNode.Parse("[\"no_denials\"]");
        });
        repo.EditVariant("smoke", "a", v => { v["note"] = "the local floor"; v["model"] = "qwen-coder"; v["provider"] = "local-qcoder"; });
        repo.RenameVariant("smoke", "a", "floor");
        repo.EditVariant("smoke", "b", v => { v["model"] = "glm"; v["provider"] = "local-glm"; });
        if (third) repo.AddVariant("smoke", """{ "id": "c", "runner": "nb", "harness": "nb", "provider": "local-glm", "model": "glm-air", "bundle": { "path": "bundles/smoke" } }""");
        if (!twoVariants) repo.RemoveVariant("smoke", "b");
        repo.EditJson("smoke/tasks/uses-bash.json", t => t["checks"] = JsonNode.Parse("{\"used-bash\": {\"tools_used\": [\"bash\"]}}"));
        var exam = repo.LoadExam("smoke");
        // Variants load in file-name order, b before floor; the worked batch lists the floor first, as the live variant, so the page reads in that order.
        var floor = exam.Variants.Single(v => v.Id == "floor");
        exam.Variants.Remove(floor);
        exam.Variants.Insert(0, floor);
        return exam;
    }

    public static Batch Batch(Exam exam) => new(
        "20260917-1432-smoke-k7px", exam.Id, "sha256:9c1e0000", System.Text.Json.JsonSerializer.SerializeToNode(exam.Def, Proctor.Exam.JsonOptions)!.AsObject(),
        exam.Variants.Select(v => new BatchVariant(v.Id!, v.Hash, System.Text.Json.JsonSerializer.SerializeToNode(v, Proctor.Exam.JsonOptions)!.AsObject())).ToList(),
        exam.Tasks.Select(c => new Ref(c.Id!, exam.TaskHashes[c.Id!])).ToList(), (from v in exam.Variants from c in exam.Tasks from n in Enumerable.Range(1, exam.Attempts) select new AttemptRef(v.Id!, c.Id!, n)).ToList(), "2026-09-17T14:32:00Z", "proctor run smoke", "bench",
        new() { ["proctor"] = "0.1.0", ["nb"] = "1.0.0" }, new GitInfo("3f2c1e9a", Dirty: false), new ResolvedNb("/usr/local/bin/nb", null));

    /// <summary>The worked report's stamp: two batches, the second filling what the first left, two attempts stale since plain changed, and the floor's attempts made on two nb versions. Fixed hashes, so the rendering does not move with the smoke exam's files.</summary>
    public static BuiltFrom Built() => new("2026-09-17T15:02:00Z", ["20260916-0910-smoke-q2rd", "20260917-1432-smoke-k7px"],
        new() { ["floor"] = "sha256:4b1e00d2", ["b"] = "sha256:77a90c13" },
        new() { ["loops"] = "sha256:0c3f9e01", ["plain"] = "sha256:5e6f7a8b", ["uses-bash"] = "sha256:d40b2c77" },
        Current: 18, Stale: 2,
        StaleAttempts: [new("20260916-0910-smoke-q2rd", "floor/plain/1", "task plain changed (1a2b3c4d then, 5e6f7a8b now)"), new("20260916-0910-smoke-q2rd", "b/plain/1", "task plain changed (1a2b3c4d then, 5e6f7a8b now)")],
        Mixed: new() { ["floor"] = ["nb 1.0.0, 1.0.1"] });

    /// <summary>The floor is live, marked a week before the batch at its hash as the exam has it.</summary>
    public static Live Live(Exam exam, string variant = "floor") =>
        new(new Ref(variant, exam.Variants.Single(v => v.Id == variant).Hash), "2026-09-10T09:12:00Z");

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
