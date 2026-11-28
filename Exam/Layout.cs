using System.Security.Cryptography;

namespace Auspex;

/// <summary>Where things live. Paths and file names only; see project/plans/on-disk-layout.md.</summary>
static class Layout
{
    public const string ExamsDir = "exams";
    public const string FixturesDir = "fixtures";
    public const string BankDir = "bank";
    public const string LogDir = "log";
    public const string ExamsSeenDir = "exams-seen";
    public const string ReportsDir = "reports";
    public const string WorkDir = ".auspex/work";
    /// <summary>Where a value's files from a git revision are cloned, and the paths it names copied out.</summary>
    public const string GitFilesDir = ".auspex/files";
    /// <summary>What `proctor build` leaves: each value's change from the reference build, and its scratch checkouts while it runs.</summary>
    public const string BuildsDir = ".auspex/builds";
    /// <summary>In a try: what its setup hook did to the checkout, as a patch.</summary>
    public const string SetupPatchFile = "setup.patch";
    /// <summary>Beside exam.json: which configurations of several values `proctor build` found to be their values put together, and which not.</summary>
    public const string CombinationsFile = "combinations.json";

    public const string AuspexConfigFile = "auspex.json";
    public const string ExamFile = "exam.json";
    public const string ProgramTemplateFile = "program.nb";
    /// <summary>Beside the definition in the earlier shape, and read only to say so: a task per file.</summary>
    public const string TasksDir = "tasks";
    /// <summary>In a try: a directory per stage, by its place and id.</summary>
    public const string StagesDir = "stages";
    /// <summary>Beside exam.json: one file per prediction: a named configuration and the changes expected of it (plans/axes-build.md, slice 6).</summary>
    public const string PredictionsDir = "predictions";
    /// <summary>Beside exam.json: the named configurations retired, each with its reason and date (`proctor retire`).</summary>
    public const string RetiredFile = "retired.json";
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
    /// <summary>In an attempt: every grade it has been given, one file per standard it was graded under (plans/axes-build.md, slice 5).</summary>
    public const string GradesDir = "grades";
    public const string FrictionFile = "friction.json";
    /// <summary>What a reader was sent and answered about an attempt: friction-reading.&lt;judge&gt;.&lt;hash&gt;.json, the cache and the record.</summary>
    public const string ReadingPrefix = "friction-reading";

    /// <summary>Through Harbor: the compiled tasks per log entry, Harbor's own job per try, and what ingest lifts out of the trial.</summary>
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
    public static string Fixture(string root, string fixtureId) => Path.Combine(root, FixturesDir, fixtureId);
    /// <summary>The bank: every try of an exam, each under an id of its own. Its manifests say which configuration it ran as.</summary>
    public static string Bank(string root, string examId) => Path.Combine(root, BankDir, examId);
    /// <summary>One try: every stage that applies in its configuration, once, in one workspace. Holds the stages, the try's hooks' output and what setup left.</summary>
    public static string Try(string root, string examId, string id) => Path.Combine(Bank(root, examId), id);
    /// <summary>One stage of a try: under the try by its place among the exam's stages and its id, so the directory lists in the order the stages ran.</summary>
    public static string Attempt(string root, string examId, string stage, IReadOnlyList<string> stages, string id) =>
        Path.Combine(Try(root, examId, id), StagesDir, $"{stages.ToList().IndexOf(stage) + 1}-{stage}");
    /// <summary>The checkout a try runs in, which its stages share.</summary>
    public static string Work(string root, string examId, string id) => Path.Combine(root, WorkDir, examId, id);
    /// <summary>A stage's workspace rebuilt for grading: the fixture with the stage's diff on it, since the try's own checkout only holds what the last stage left.</summary>
    public static string WorkAsStageLeftIt(string root, string examId, string stageDir, string id) => Path.Combine(root, WorkDir, examId, $"{id}.stages", stageDir);
    /// <summary>What one `proctor run` did: entry.json, its status counts, its configuration hooks' output, and the Harbor tasks it compiled.</summary>
    public static string LogEntry(string root, string entryId) => Path.Combine(root, LogDir, entryId);
    public static string LogEntryFileIn(string root, string entryId) => Path.Combine(LogEntry(root, entryId), LogEntryFile);
    /// <summary>The exam's own definition as it stood at a hash, kept once per hash.</summary>
    public static string ExamSeen(string root, string examHash) =>
        Path.Combine(root, ExamsSeenDir, $"sha256-{(examHash.StartsWith("sha256:") ? examHash[7..] : examHash)[..12]}.json");
    /// <summary>The one Harbor task a configuration's tries run as, compiled once per log entry: every stage that applies a step of it. Named for the exam, which no stage of it can be.</summary>
    public static string HarborJobTask(string entryDir, string key, string examId) => Path.Combine(entryDir, HarborDir, "tasks", key, examId);
    /// <summary>nb's programs for one try through Harbor, one per stage by its place, which nb's Harbor agent uploads and runs in order.</summary>
    public const string StageProgramsDir = "programs";
    /// <summary>The exploration report of an exam: the latest here, each earlier one moved into a directory of its own beside it, named by when it was built.</summary>
    public static string ReportExplore(string root, string examId) => Path.Combine(root, ReportsDir, examId, "explore");

    /// <summary>Date prefix so ls sorts, the exam id so a human can tell them apart, four random characters so it is an id.</summary>
    public static string NewLogEntryId(string examId, DateTime utcNow) =>
        $"{utcNow:yyyyMMdd-HHmm}-{examId}-{RandomSuffix(4)}";

    public static string NewAttemptId() => RandomSuffix(16);

    /// <summary>A try's id: when it was made and four random characters, so it sorts by time, is made offline and never changes.</summary>
    public static string NewTryId(DateTime utcNow) => $"{utcNow:yyyyMMdd-HHmm}-{RandomSuffix(4)}";

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
