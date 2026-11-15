using System.Text.Json;

namespace Proctor;

/// <summary>One row of results.jsonl: coordinates on every row, the batch that made the attempt included, no transcript text.</summary>
record ResultRow(
    string Id, string Variant, string Task, int Attempt,
    string Status, string? StatusReason, string? ExitReason,
    UsageRow? Usage, int? ToolCalls, int? DeniedCalls, long? DurationMs,
    Dictionary<string, string>? Checks, bool? Pass, Dictionary<string, string>? Reasons, string? Invalid = null,
    Dictionary<string, List<string>>? Labels = null, string? Undecided = null, string? Batch = null)
{
    /// <summary>Graded and counted: an invalid run is graded but does not count. A counted run may still be undecided (Pass null).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Analysed => Checks is not null && Invalid is null;

    /// <summary>Counted and decided: the runs a pass rate is over.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Decided => Analysed && Pass is not null;
}

/// <param name="CostUsd">What the agent reported its run cost (Claude Code its own count, Codex as Harbor priced it); nb reports none.</param>
record UsageRow(long? Input, long? Output, long? Total, bool Estimated, double? CostUsd = null);

static class Results
{
    /// <summary>Read the given attempts into rows, in the exam's order of variants and tasks, then by attempt. Grades come from checks.json; attempts without one are not analysed.</summary>
    public static List<ResultRow> Collect(string root, Exam exam, IEnumerable<PoolAttempt> attempts)
    {
        var rows = new List<ResultRow>();
        var ordered = attempts.OrderBy(a => exam.Variants.FindIndex(v => v.Id == a.Variant)).ThenBy(a => exam.Tasks.FindIndex(t => t.Id == a.Task)).ThenBy(a => a.Attempt);
        foreach (var a in ordered)
        {
            var (c, n) = (exam.Tasks.First(t => t.Id == a.Task), a.Attempt);
            var attemptDir = a.Dir(root);
            var manifestFile = Path.Combine(attemptDir, Layout.ManifestFile);
            var manifest = File.Exists(manifestFile) ? Runner.ReadManifest(manifestFile) : null;
            var status = Runner.ReadStatus(attemptDir);
            var trailer = status == AttemptStatus.Completed && Transcript.Exists(attemptDir) ? Transcript.Read(attemptDir).Trailer : null;
            var checks = status == AttemptStatus.Completed ? Grade.ReadChecks(attemptDir) : null;
            rows.Add(new ResultRow(
                Id: manifest?.Id ?? "",
                Variant: a.Variant, Task: c.Id!, Attempt: n,
                Status: status,
                StatusReason: manifest?.StatusReason ?? (checks is null && status == AttemptStatus.Completed ? "not graded" : null),
                ExitReason: trailer?.ExitReason,
                Usage: trailer is null ? null : new UsageRow(trailer.Input, trailer.Output, trailer.Total, trailer.Estimated, trailer.CostUsd),
                ToolCalls: trailer?.ToolCalls,
                DeniedCalls: trailer is null ? null : trailer.Denied,
                DurationMs: manifest?.DurationMs,
                Checks: checks?.ToDictionary(k => k.Key, k => k.Value.Result),
                Pass: checks is null ? null : Grade.Pass(checks, exam.Grading.Pass!),
                // The headline checks lead, so a failed run's first reason is what failed it, not a guardrail declared before it.
                Reasons: checks?.Where(k => k.Value.Result != Verdict.Pass).OrderBy(k => !exam.Grading.Pass!.Contains(k.Key))
                    .ToDictionary(k => k.Key, k => k.Value.Reason) is { Count: > 0 } r ? r : null,
                Invalid: checks is null ? null : Grade.Invalid(checks, exam.Grading.Validity),
                Labels: exam.LabelsFor(c) is { Count: > 0 } labels ? labels : null,
                Undecided: checks is null ? null : Grade.Undecided(checks, exam.Grading.Pass!),
                Batch: a.Batch.Id));
        }
        return rows;
    }

    static readonly JsonSerializerOptions RowOptions = new(Exam.JsonOptions) { WriteIndented = false };

    public static void Write(string file, IEnumerable<ResultRow> rows) =>
        File.WriteAllLines(file, rows.Select(r => JsonSerializer.Serialize(r, RowOptions)));

    public static List<ResultRow> Read(string file) =>
        File.ReadAllLines(file).Where(l => l.Length > 0).Select(l => JsonSerializer.Deserialize<ResultRow>(l, RowOptions)!).ToList();
}
