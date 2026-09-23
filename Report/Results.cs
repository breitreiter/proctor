using System.Text.Json;

namespace Proctor;

/// <summary>One row of results.jsonl: coordinates on every row, no transcript text.</summary>
record ResultRow(
    string RunId, string Arm, string Task, int Sample,
    string Status, string? StatusReason, string? ExitReason,
    UsageRow? Usage, int? ToolCalls, int? DeniedCalls, long? DurationMs,
    Dictionary<string, string>? Checks, bool? Pass, Dictionary<string, string>? Reasons, string? Invalid = null,
    Dictionary<string, List<string>>? Labels = null, string? Undecided = null)
{
    /// <summary>Graded and counted: an invalid run is graded but does not count. A counted run may still be undecided (Pass null).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Analysed => Checks is not null && Invalid is null;

    /// <summary>Counted and decided: the runs a pass rate is over.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Decided => Analysed && Pass is not null;
}

record UsageRow(long? Input, long? Output, long? Total, bool Estimated);

static class Results
{
    /// <summary>Read every cell of an experiment into rows, in declared order. Grades come from checks.json; cells without one are not analysed.</summary>
    public static List<ResultRow> Collect(string root, Experiment experiment, Suite suite)
    {
        var experimentDir = Layout.Experiment(root, experiment.Id);
        var rows = new List<ResultRow>();
        foreach (var (arm, c, sample) in Runner.Cells(suite))
        {
            var cellDir = Layout.Cell(experimentDir, arm.Id!, c.Id!, sample);
            var manifestFile = Path.Combine(cellDir, Layout.ManifestFile);
            var manifest = File.Exists(manifestFile) ? Runner.ReadManifest(manifestFile) : null;
            var status = Runner.ReadStatus(cellDir);
            var transcriptFile = Path.Combine(cellDir, Layout.TranscriptFile);
            var trailer = status == CellStatus.Completed && File.Exists(transcriptFile) ? Transcript.Read(cellDir).Trailer : null;
            var checks = status == CellStatus.Completed ? Grade.ReadChecks(cellDir) : null;
            rows.Add(new ResultRow(
                RunId: manifest?.RunId ?? "",
                Arm: arm.Id!, Task: c.Id!, Sample: sample,
                Status: status,
                StatusReason: manifest?.StatusReason ?? (checks is null && status == CellStatus.Completed ? "not graded" : null),
                ExitReason: trailer?.ExitReason,
                Usage: trailer is null ? null : new UsageRow(trailer.Input, trailer.Output, trailer.Total, trailer.Estimated),
                ToolCalls: trailer?.ToolCalls,
                DeniedCalls: trailer is null ? null : trailer.Denied,
                DurationMs: manifest?.DurationMs,
                Checks: checks?.ToDictionary(k => k.Key, k => k.Value.Result),
                Pass: checks is null ? null : Grade.Pass(checks, suite.Grading.Pass!),
                Reasons: checks?.Where(k => k.Value.Result != Verdict.Pass).ToDictionary(k => k.Key, k => k.Value.Reason) is { Count: > 0 } r ? r : null,
                Invalid: checks is null ? null : Grade.Invalid(checks, suite.Grading.Validity),
                Labels: suite.LabelsFor(c) is { Count: > 0 } labels ? labels : null,
                Undecided: checks is null ? null : Grade.Undecided(checks, suite.Grading.Pass!)));
        }
        return rows;
    }

    static readonly JsonSerializerOptions RowOptions = new(Suite.JsonOptions) { WriteIndented = false };

    public static void Write(string file, IEnumerable<ResultRow> rows) =>
        File.WriteAllLines(file, rows.Select(r => JsonSerializer.Serialize(r, RowOptions)));

    public static List<ResultRow> Read(string file) =>
        File.ReadAllLines(file).Where(l => l.Length > 0).Select(l => JsonSerializer.Deserialize<ResultRow>(l, RowOptions)!).ToList();
}
