using System.Security.Cryptography;

namespace Proctor;

/// <summary>Where things live. Paths and file names only; see project/plans/on-disk-layout.md.</summary>
static class Layout
{
    public const string SuitesDir = "suites";
    /// <summary>The spellings before suite/task (2026-09-23); a reader accepts them for one release, a writer never uses them.</summary>
    public const string LegacySuitesDir = "evals", LegacySuiteFile = "eval.json", LegacyTasksDir = "cases";
    public const string FixturesDir = "fixtures";
    public const string RunsDir = "runs";
    public const string ReportsDir = "reports";
    public const string WorkDir = ".proctor/work";
    public const string BundlesDir = ".proctor/bundles";

    public const string ProctorConfigFile = "proctor.json";
    public const string SuiteFile = "suite.json";
    public const string ProgramTemplateFile = "program.nb";
    public const string TasksDir = "tasks";
    public const string BaselineFile = "baseline.json";
    public const string FixtureFile = "fixture.json";

    public const string ExperimentFile = "experiment.json";
    public const string StatusCountsFile = "status.json";
    public const string HooksDir = "hooks";

    public const string ManifestFile = "manifest.json";
    public const string StatusFile = "status";
    public const string ProgramFile = "program.nb";
    public const string CompiledProgramFile = "program.jsonl";
    public const string TranscriptFile = "transcript.jsonl";
    public const string StderrFile = "stderr.txt";
    public const string DiffFile = "diff.patch";
    public const string ChecksFile = "checks.json";
    public const string VerdictsDir = "verdicts";

    public const string ResultsFile = "results.jsonl";
    public const string StatsFile = "stats.json";
    public const string ReportHtmlFile = "report.html";
    public const string SummaryFile = "summary.md";

    /// <summary>suites/, or evals/ when only the old spelling exists.</summary>
    public static string Suites(string root) => Existing(Path.Combine(root, SuitesDir), Path.Combine(root, LegacySuitesDir));
    public static string Suite(string root, string suiteId) => Path.Combine(Suites(root), suiteId);
    /// <summary>The suite's definition file in its directory: suite.json, or eval.json when only that exists.</summary>
    public static string SuiteFileIn(string suiteDir) => Existing(Path.Combine(suiteDir, SuiteFile), Path.Combine(suiteDir, LegacySuiteFile));
    /// <summary>The suite's tasks directory: tasks/, or cases/ when only that exists.</summary>
    public static string TasksDirIn(string suiteDir) => Existing(Path.Combine(suiteDir, TasksDir), Path.Combine(suiteDir, LegacyTasksDir));
    static string Existing(string preferred, string legacy) => !Path.Exists(preferred) && Path.Exists(legacy) ? legacy : preferred;
    public static string Fixture(string root, string fixtureId) => Path.Combine(root, FixturesDir, fixtureId);
    public static string Experiment(string root, string experimentId) => Path.Combine(root, RunsDir, experimentId);
    public static string Arm(string experimentDir, string arm) => Path.Combine(experimentDir, arm);
    public static string Cell(string experimentDir, string arm, string task, int sample) =>
        Path.Combine(experimentDir, arm, task, sample.ToString());
    public static string Work(string root, string experimentId, string arm, string task, int sample) =>
        Path.Combine(root, WorkDir, experimentId, arm, task, sample.ToString());
    public static string GitBundle(string root, string rev) => Path.Combine(root, BundlesDir, rev);
    public static string ReportData(string root, string experimentId) =>
        Path.Combine(root, ReportsDir, "data", experimentId);

    /// <summary>Date prefix so ls sorts, the suite id so a human can tell them apart, four random characters so it is an id.</summary>
    public static string NewExperimentId(string suiteId, DateTime utcNow) =>
        $"{utcNow:yyyyMMdd-HHmm}-{suiteId}-{RandomSuffix(4)}";

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
