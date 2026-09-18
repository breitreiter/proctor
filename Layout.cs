using System.Security.Cryptography;

namespace Proctor;

/// <summary>Where things live. Paths and file names only; see project/plans/on-disk-layout.md.</summary>
static class Layout
{
    public const string EvalsDir = "evals";
    public const string RunsDir = "runs";
    public const string ReportsDir = "reports";
    public const string WorkDir = ".proctor/work";

    public const string ProctorConfigFile = "proctor.json";
    public const string EvalFile = "eval.json";
    public const string ProgramTemplateFile = "program.nb";
    public const string CasesDir = "cases";

    public const string ExperimentFile = "experiment.json";
    public const string StatusCountsFile = "status.json";
    public const string HooksDir = "hooks";

    public const string ManifestFile = "manifest.json";
    public const string StatusFile = "status";
    public const string ProgramFile = "program.nb";
    public const string TranscriptFile = "transcript.jsonl";
    public const string StderrFile = "stderr.txt";
    public const string DiffFile = "diff.patch";
    public const string ChecksFile = "checks.json";

    public const string ResultsFile = "results.jsonl";
    public const string StatsFile = "stats.json";
    public const string ReportHtmlFile = "report.html";
    public const string SummaryFile = "summary.md";

    public static string Evals(string root) => Path.Combine(root, EvalsDir);
    public static string Eval(string root, string evalId) => Path.Combine(root, EvalsDir, evalId);
    public static string Experiment(string root, string experimentId) => Path.Combine(root, RunsDir, experimentId);
    public static string Arm(string experimentDir, string arm) => Path.Combine(experimentDir, arm);
    public static string Cell(string experimentDir, string arm, string @case, int sample) =>
        Path.Combine(experimentDir, arm, @case, sample.ToString());
    public static string Work(string root, string experimentId, string arm, string @case, int sample) =>
        Path.Combine(root, WorkDir, experimentId, arm, @case, sample.ToString());
    public static string ReportData(string root, string experimentId) =>
        Path.Combine(root, ReportsDir, "data", experimentId);

    /// <summary>Date prefix so ls sorts, the eval id so a human can tell them apart, four random characters so it is an id.</summary>
    public static string NewExperimentId(string evalId, DateTime utcNow) =>
        $"{utcNow:yyyyMMdd-HHmm}-{evalId}-{RandomSuffix(4)}";

    public static string NewRunId() => RandomSuffix(16);

    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
    private const string Hex = "0123456789abcdef";

    private static string RandomSuffix(int length)
    {
        var alphabet = length > 4 ? Hex : Alphabet;
        return string.Create(length, alphabet, (span, a) =>
        {
            for (var i = 0; i < span.Length; i++) span[i] = a[RandomNumberGenerator.GetInt32(a.Length)];
        });
    }
}
