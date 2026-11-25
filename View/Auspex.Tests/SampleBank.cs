namespace Auspex.Tests;

/// <summary>A proctor root made for a test: one exam with its definition and live mark, two setups, one try with checks,
/// a friction.json and a trajectory, and one failed try with a trajectory only.</summary>
public sealed class SampleBank : IDisposable
{
    public const string Exam = "sample";
    public const string Try = "/sample/live-setup/task-one/1";
    public const string GreenTry = "/sample/live-setup/task-one/2";
    /// <summary>An exam of stages: one try, its first stage completed and graded, its second not reached.</summary>
    public const string StagedTry = "/staged/live-setup/1";
    public const string Stage = "/staged/live-setup/1/write-note", StageNotReached = "/staged/live-setup/1/read-note";

    readonly string _temp = Directory.CreateTempSubdirectory("auspex-test-").FullName;

    public string Root { get; }

    public SampleBank()
    {
        Root = Path.Combine(_temp, "proctor");
        Write(Path.Combine(Root, "exams", Exam, "exam.json"), """{"id": "sample", "description": "A made-up exam about the docs site on :8080."}""");
        Write(Path.Combine(Root, "exams", Exam, "live.json"), """{"variant": "live-setup", "hash": "aaaa", "since": "2026-01-01T00:00:00Z"}""");

        var coded = Path.Combine(Root, "bank", Exam, "live-setup", "task-one", "1");
        Write(Path.Combine(coded, "manifest.json"), """{"id": "x1", "exam": "sample", "log": "20260101-0000-sample-aaaa", "variant": {"id": "live-setup", "hash": "aaaa"}, "task": {"id": "task-one", "hash": "bbbb"}, "attempt": 1, "model": "a-model", "status": "completed"}""");
        Write(Path.Combine(coded, "status"), "completed\n");
        Write(Path.Combine(coded, "core.json"), """{"cost_usd": 0.0123}""");
        Write(Path.Combine(coded, "checks.json"), """
            {"acceptance": {"result": "fail", "reason": "1 failed", "describes": "the acceptance tests pass", "why": "the task is only done when its tests pass", "script": "exams/sample/checks/acceptance.sh",
                            "evidence": [{"step": "3"}]},
             "scope": {"result": "fail", "reason": "touched 1 outside expected: README.md", "evidence": [{"file": "README.md"}]},
             "exit_ok": {"result": "pass", "reason": "exit_reason=ok"}}
            """);
        Write(Path.Combine(coded, "trajectory.json"), """
            {"steps": [
              {"step_id": 1, "source": "user", "message": "Do the <task> & report."},
              {"step_id": 2, "source": "agent", "message": "",
               "tool_calls": [{"tool_call_id": "c1", "function_name": "Bash", "arguments": {"command": "curl -s localhost:8080/docs; curl -s localhost:8081"}}],
               "observation": {"results": [{"source_call_id": "c1", "content": "<title>Not\nfound</title>"}]},
               "metrics": {"cost_usd": 0.004}},
              {"step_id": 3, "source": "agent", "message": "Done.",
               "tool_calls": [{"tool_call_id": "c2", "function_name": "Read", "arguments": {"file_path": "/app/a.py", "limit": 5}}],
               "observation": {"results": [{"source_call_id": "c2", "content": "print(1)"}]}}
            ]}
            """);
        Write(Path.Combine(coded, "friction.json"), """
            {"schema": "friction/2", "attempt": {"exam": "sample", "variant": "live-setup", "task": "task-one", "attempt": "1"},
             "detector": {"version": "0.10", "labels": [{"name": "fell-back", "describes": "a step where the agent used a substitute within two steps of a call to the surface that failed"}]},
             "reader": {"model": "a-model", "labels": [{"name": "fixed-own-work", "describes": "a step where the agent fixed its own earlier work"}]},
             "summary": {"steps": 2, "calls": 2, "points": 2, "flagged_steps": 1, "by_label": {"fell-back": 1, "fixed-own-work": 1},
                         "surface": {"reached": true, "used": true, "worked": false, "used_substitute": true}},
             "points": [
               {"id": "p1", "label": "fell-back", "steps": ["2", "2"], "calls": ["s2.1"],
                "evidence": [{"step": "2", "call": "s2.1", "quote": "curl -s localhost:8081", "where": "command", "rule": "substitute-after-failed-surface",
                              "because": "used curl -s localhost:8081, a declared substitute, in the same step as the call to localhost:8080 failed (Not found)"},
                             {"step": "2", "call": "s2.1", "quote": "Notfound", "where": "result"}],
                "rules": ["substitute-after-failed-surface"], "detail": ["curl then curl"], "cost_usd": 0.004, "source": {"kind": "rule", "detector": "0.9"}},
               {"id": "p2", "label": "fixed-own-work", "steps": ["2", "2"], "calls": ["s2.1"],
                "evidence": [{"step": "2", "call": "s2.1", "quote": "a quote that is not there", "where": "result"}],
                "rules": [], "detail": [], "notes": ["tries again"], "cost_usd": null, "source": {"kind": "model", "model": "a-model"}}
             ]}
            """);

        // A try where every check passed: the page opens on the transcript, the checks one click away.
        var green = Path.Combine(Root, "bank", Exam, "live-setup", "task-one", "2");
        Write(Path.Combine(green, "manifest.json"), """{"id": "x3", "exam": "sample", "log": "20260101-0000-sample-aaaa", "variant": {"id": "live-setup", "hash": "aaaa"}, "task": {"id": "task-one", "hash": "bbbb"}, "attempt": 2, "status": "completed"}""");
        Write(Path.Combine(green, "status"), "completed\n");
        Write(Path.Combine(green, "checks.json"), """{"acceptance": {"result": "pass", "reason": "all passed"}}""");
        Write(Path.Combine(green, "trajectory.json"), """{"steps": [{"step_id": 1, "source": "user", "message": "Do it."}, {"step_id": 2, "source": "agent", "message": "Done."}]}""");

        var bare = Path.Combine(Root, "bank", Exam, "other-setup", "task-one", "1");
        Write(Path.Combine(bare, "manifest.json"), """{"id": "x2", "exam": "sample", "log": "20260101-0000-sample-aaaa", "variant": {"id": "other-setup", "hash": "cccc"}, "task": {"id": "task-one", "hash": "bbbb"}, "attempt": 1, "status": "failed"}""");
        Write(Path.Combine(bare, "status"), "failed\n");
        Write(Path.Combine(bare, "trajectory.json"), """{"steps": [{"step_id": 1, "source": "user", "message": "Do it."}]}""");

        // An exam of stages, as proctor lays one out: bank/<exam>/<variant>/<n>/stages/<i>-<id>/.
        Write(Path.Combine(Root, "exams", "staged", "exam.json"), """{"id": "staged", "description": "One job of two stages.", "chained": true}""");
        var tryDir = Path.Combine(Root, "bank", "staged", "live-setup", "1");
        var first = Path.Combine(tryDir, "stages", "1-write-note");
        Write(Path.Combine(first, "manifest.json"), """{"id": "s1", "exam": "staged", "variant": {"id": "live-setup", "hash": "aaaa"}, "task": {"id": "write-note", "hash": "jjjj"}, "attempt": 1, "stage": 1, "status": "completed"}""");
        Write(Path.Combine(first, "status"), "completed\n");
        Write(Path.Combine(first, "checks.json"), """{"exit_ok": {"result": "fail", "reason": "the attempt ended with 'max_tool_calls'", "describes": "the attempt ends with 'ok'", "why": "An attempt that does not end on its own answered nothing."}}""");
        Write(Path.Combine(first, "trajectory.json"), """{"steps": [{"step_id": 1, "source": "user", "message": "Write the note."}, {"step_id": 2, "source": "agent", "message": "Which file?"}, {"step_id": 3, "source": "user", "message": "note.txt", "extra": {"source": "oracle", "keys": ["which-file"]}}, {"step_id": 4, "source": "agent", "message": "And where?"}], "extra": {"result": {"exit_reason": "oracle_miss"}}}""");
        var second = Path.Combine(tryDir, "stages", "2-read-note");
        Write(Path.Combine(second, "manifest.json"), """{"id": "s2", "exam": "staged", "variant": {"id": "live-setup", "hash": "aaaa"}, "task": {"id": "read-note", "hash": "jjjj"}, "attempt": 1, "stage": 2, "status": "skipped", "status_reason": "not reached: stage write-note was stopped (max_tool_calls), and this exam's stages are chained"}""");
        Write(Path.Combine(second, "status"), "skipped\n");
    }

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    static void Write(string file, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
    }
}
