using System.Security.Cryptography;

namespace Proctor;

/// <summary>Where things live. Paths and file names only; see project/plans/on-disk-layout.md.</summary>
static class Layout
{
    public const string ExamsDir = "exams";
    public const string FixturesDir = "fixtures";
    public const string BankDir = "bank";
    public const string LogDir = "log";
    public const string ExamsSeenDir = "exams-seen";
    public const string ReportsDir = "reports";
    public const string WorkDir = ".proctor/work";
    public const string BundlesDir = ".proctor/bundles";

    public const string ProctorConfigFile = "proctor.json";
    public const string ExamFile = "exam.json";
    public const string ProgramTemplateFile = "program.nb";
    public const string TasksDir = "tasks";
    public const string StagesDir = "stages";
    public const string VariantsDir = "variants";
    public const string LiveFile = "live.json";
    public const string FixtureFile = "fixture.json";

    public const string LogEntryFile = "entry.json";
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

    /// <summary>An agent variant's attempts: the compiled Harbor tasks per log entry, Harbor's own job per attempt, and what ingest lifts out of the trial.</summary>
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
    public const string SummaryFile = "summary.md";

    public static string Exams(string root) => Path.Combine(root, ExamsDir);
    public static string Exam(string root, string examId) => Path.Combine(Exams(root), examId);
    public static string ExamFileIn(string examDir) => Path.Combine(examDir, ExamFile);
    public static string TasksDirIn(string examDir) => Path.Combine(examDir, TasksDir);
    public static string VariantsDirIn(string examDir) => Path.Combine(examDir, VariantsDir);
    public static string Fixture(string root, string fixtureId) => Path.Combine(root, FixturesDir, fixtureId);
    /// <summary>The bank: every attempt of an exam, by variant, task and number. Numbers run on, so the path names one attempt for good.</summary>
    public static string Bank(string root, string examId) => Path.Combine(root, BankDir, examId);
    /// <summary>
    /// An attempt's directory. Of a task exam, by variant, task and number. Of a staged exam (its stage ids given, in
    /// order), the attempt is one stage of a try: the try is the variant's nth, and the stage sits under it by its place
    /// and id, so the directory lists in the order the stages ran.
    /// </summary>
    public static string Attempt(string root, string examId, string variant, string task, int n, IReadOnlyList<string>? stages = null) =>
        stages is null ? Path.Combine(Bank(root, examId), variant, task, n.ToString())
        : Path.Combine(Try(root, examId, variant, n), StagesDir, $"{stages.ToList().IndexOf(task) + 1}-{task}");
    /// <summary>One try of a staged exam: every stage once, in one workspace. Holds the stages, the try's hooks' output and its diff.</summary>
    public static string Try(string root, string examId, string variant, int n) => Path.Combine(Bank(root, examId), variant, n.ToString());
    /// <summary>The checkout an attempt runs in. A staged exam's stages share their try's.</summary>
    public static string Work(string root, string examId, string variant, string task, int n, IReadOnlyList<string>? stages = null) =>
        stages is null ? Path.Combine(root, WorkDir, examId, variant, task, n.ToString())
        : Path.Combine(root, WorkDir, examId, variant, n.ToString());
    /// <summary>A stage's workspace rebuilt for grading: the fixture with the stage's diff on it, since the try's own checkout only holds what the last stage left.</summary>
    public static string WorkAsStageLeftIt(string root, string examId, string variant, int n, string stageDir) =>
        Path.Combine(root, WorkDir, examId, variant, $"{n}.stages", stageDir);
    /// <summary>What one `proctor run` did: entry.json, its status counts, its variant hooks' output, and the Harbor tasks it compiled.</summary>
    public static string LogEntry(string root, string entryId) => Path.Combine(root, LogDir, entryId);
    public static string LogEntryFileIn(string root, string entryId) => Path.Combine(LogEntry(root, entryId), LogEntryFile);
    /// <summary>The exam's own definition as it stood at a hash, kept once per hash.</summary>
    public static string ExamSeen(string root, string examHash) =>
        Path.Combine(root, ExamsSeenDir, $"sha256-{(examHash.StartsWith("sha256:") ? examHash[7..] : examHash)[..12]}.json");
    /// <summary>The Harbor task a variant runs a task as, compiled once per log entry.</summary>
    public static string HarborTask(string entryDir, string variant, string task) => Path.Combine(entryDir, HarborDir, "tasks", variant, task);
    /// <summary>The one Harbor task a variant runs a staged exam as: every stage a step of it. Named for the exam, which no stage of it can be.</summary>
    public static string HarborJobTask(string entryDir, string variant, string examId) => Path.Combine(entryDir, HarborDir, "tasks", variant, examId);
    /// <summary>An nb agent variant's programs for one try, one per stage by its place, which nb's Harbor agent uploads and runs in order.</summary>
    public const string StageProgramsDir = "programs";
    public static string GitBundle(string root, string rev) => Path.Combine(root, BundlesDir, rev);
    /// <summary>The files a bundle's paths name, copied out of its revision's clone; named by the bundle's identity so two subsets of one revision sit apart.</summary>
    public static string GitBundleSubset(string root, string rev, string identityHash) => Path.Combine(root, BundlesDir, $"{rev}-{identityHash[..12]}");
    /// <summary>The exploration report of an exam: the latest here, each earlier one moved into a directory of its own beside it, named by when it was built.</summary>
    public static string ReportExplore(string root, string examId) => Path.Combine(root, ReportsDir, examId, "explore");

    /// <summary>Date prefix so ls sorts, the exam id so a human can tell them apart, four random characters so it is an id.</summary>
    public static string NewLogEntryId(string examId, DateTime utcNow) =>
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
