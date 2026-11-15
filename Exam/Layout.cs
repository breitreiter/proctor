using System.Security.Cryptography;

namespace Proctor;

/// <summary>Where things live. Paths and file names only; see project/plans/on-disk-layout.md.</summary>
static class Layout
{
    public const string ExamsDir = "exams";
    public const string FixturesDir = "fixtures";
    public const string AttemptsDir = "attempts";
    public const string ReportsDir = "reports";
    public const string WorkDir = ".proctor/work";
    public const string BundlesDir = ".proctor/bundles";

    public const string ProctorConfigFile = "proctor.json";
    public const string ExamFile = "exam.json";
    public const string ProgramTemplateFile = "program.nb";
    public const string TasksDir = "tasks";
    public const string VariantsDir = "variants";
    public const string BaselineFile = "baseline.json";
    public const string FixtureFile = "fixture.json";

    public const string BatchFile = "batch.json";
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
    public const string FrictionFile = "friction.json";
    /// <summary>What a reader was sent and answered about an attempt: friction-reading.&lt;judge&gt;.&lt;hash&gt;.json, the cache and the record.</summary>
    public const string ReadingPrefix = "friction-reading";

    /// <summary>An agent variant's attempts: the compiled Harbor tasks per batch, Harbor's own job per attempt, and what ingest lifts out of the trial.</summary>
    public const string HarborDir = "harbor";
    public const string HarborJobFile = "job.json";
    public const string HarborLogFile = "harbor.log";
    public const string NbLogFile = "nb.jsonl";
    public const string TrajectoryFile = "trajectory.json";
    public const string CoreFile = "core.json";
    public const string RewardFile = "reward.json";
    public const string AgentLogsDir = "agent";
    public const string UserAgentLogsDir = "user-agent";
    public const string OracleUserFile = "oracle-user.json";
    public const string VerifierDir = "verifier";

    public const string ResultsFile = "results.jsonl";
    public const string StatsFile = "stats.json";
    public const string ReportHtmlFile = "report.html";
    public const string SummaryFile = "summary.md";

    public static string Exams(string root) => Path.Combine(root, ExamsDir);
    public static string Exam(string root, string examId) => Path.Combine(Exams(root), examId);
    public static string ExamFileIn(string examDir) => Path.Combine(examDir, ExamFile);
    public static string TasksDirIn(string examDir) => Path.Combine(examDir, TasksDir);
    public static string VariantsDirIn(string examDir) => Path.Combine(examDir, VariantsDir);
    public static string Fixture(string root, string fixtureId) => Path.Combine(root, FixturesDir, fixtureId);
    public static string Batch(string root, string batchId) => Path.Combine(root, AttemptsDir, batchId);
    public static string Variant(string batchDir, string variant) => Path.Combine(batchDir, variant);
    public static string Attempt(string batchDir, string variant, string task, int n) =>
        Path.Combine(batchDir, variant, task, n.ToString());
    public static string Work(string root, string batchId, string variant, string task, int n) =>
        Path.Combine(root, WorkDir, batchId, variant, task, n.ToString());
    /// <summary>The Harbor task a variant runs a task as, compiled once per batch.</summary>
    public static string HarborTask(string batchDir, string variant, string task) => Path.Combine(batchDir, HarborDir, "tasks", variant, task);
    public static string GitBundle(string root, string rev) => Path.Combine(root, BundlesDir, rev);
    /// <summary>The files a bundle's paths name, copied out of its revision's clone; named by the bundle's identity so two subsets of one revision sit apart.</summary>
    public static string GitBundleSubset(string root, string rev, string identityHash) => Path.Combine(root, BundlesDir, $"{rev}-{identityHash[..12]}");
    public static string ReportData(string root, string batchId) =>
        Path.Combine(root, ReportsDir, "data", batchId);

    /// <summary>Date prefix so ls sorts, the exam id so a human can tell them apart, four random characters so it is an id.</summary>
    public static string NewBatchId(string examId, DateTime utcNow) =>
        $"{utcNow:yyyyMMdd-HHmm}-{examId}-{RandomSuffix(4)}";

    public static string NewAttemptId() => RandomSuffix(16);

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
