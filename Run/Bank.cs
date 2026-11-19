using System.Text.Json.Nodes;

namespace Proctor;

/// <summary>An attempt in an exam's bank: its coordinates, its status, the variant and task hashes it was made under, and the log entry of the run that made it, which says how nb, the bundles and Harbor were resolved for it.</summary>
record BankAttempt(LogEntry Entry, string Variant, string Task, int Attempt, string Status, string VariantHash, string TaskHash)
{
    public string Label => $"{Variant}/{Task}/{Attempt}";
    public string Dir(string root) => Layout.Attempt(root, Entry.Exam, Variant, Task, Attempt);
    /// <summary>Never finished: pending, running when the run stopped, or failed for a reason of the machine's. A completed attempt is a result, whatever its exit reason.</summary>
    public bool Unfinished => Status is AttemptStatus.Pending or AttemptStatus.Running or AttemptStatus.Failed;
}

/// <summary>An attempt left out because a hash it was made under has moved: which, and how.</summary>
record StaleAttempt(string Attempt, string Why);

/// <summary>What a verb reads: the current attempts within --variant and --tasks, and the stale ones it leaves out.</summary>
record BankSelection(List<BankAttempt> Current, List<StaleAttempt> Stale);

/// <summary>A run that stopped before it finished: its counts, and its unfinished current attempts that are still missing, which `run` continues.</summary>
record InterruptedRun(LogEntry Entry, int Finished, int Running, int Failed, List<BankAttempt> Missing)
{
    public string Account => $"{Finished} of {Entry.Attempts.Count} attempt{(Entry.Attempts.Count == 1 ? "" : "s")} finished"
        + (Running > 0 ? $", {Running} {(Running == 1 ? "was" : "were")} running" : "")
        + (Failed > 0 ? $", {Failed} failed" : "");
}

/// <summary>
/// An exam's bank is every attempt of it under bank/&lt;exam&gt;/, whenever it was made. An attempt is current when the
/// variant and task hashes it was made under are today's, and stale otherwise; only current attempts count, and only
/// current, completed ones fill the exam's attempts. A stale attempt stays where it is and is left out.
/// </summary>
static class Bank
{
    /// <summary>The log entries of an exam's runs, oldest first (an entry's id starts with its time).</summary>
    public static List<LogEntry> Entries(string root, string examId)
    {
        var logDir = Path.Combine(root, Layout.LogDir);
        if (!Directory.Exists(logDir)) return [];
        var entries = new List<LogEntry>();
        foreach (var dir in Directory.GetDirectories(logDir).Order(StringComparer.Ordinal))
        {
            var file = Path.Combine(dir, Layout.LogEntryFile);
            if (!File.Exists(file) || JsonNode.Parse(File.ReadAllText(file))?["exam"]?.GetValue<string>() != examId) continue;
            entries.Add(Runner.LoadEntry(root, Path.GetFileName(dir)));
        }
        return entries;
    }

    /// <summary>Every attempt each run of the exam set out to make. One never started has no manifest, and was to be made under the hashes its log entry recorded.</summary>
    public static List<BankAttempt> Of(string root, Exam exam) =>
        Entries(root, exam.Id).SelectMany(entry => entry.Attempts.Select(a =>
        {
            var dir = Layout.Attempt(root, exam.Id, a.Variant, a.Task, a.Attempt);
            var manifestFile = Path.Combine(dir, Layout.ManifestFile);
            var manifest = File.Exists(manifestFile) ? Runner.ReadManifest(manifestFile) : null;
            return new BankAttempt(entry, a.Variant, a.Task, a.Attempt, Runner.ReadStatus(dir),
                manifest?.Variant.Hash ?? entry.Variants.First(v => v.Id == a.Variant).Hash,
                manifest?.Task.Hash ?? entry.Tasks.First(t => t.Id == a.Task).Hash);
        })).ToList();

    /// <summary>Null when the attempt is current; otherwise which hash moved since it was made.</summary>
    public static string? Stale(Exam exam, BankAttempt a) =>
        exam.Variants.FirstOrDefault(v => v.Id == a.Variant) is not { } variant ? $"variant {a.Variant} has no file now"
        : variant.Hash != a.VariantHash ? $"variant {a.Variant} changed ({Short(a.VariantHash)} then, {Short(variant.Hash)} now)"
        : !exam.TaskHashes.TryGetValue(a.Task, out var task) ? $"task {a.Task} is no longer in the exam"
        : task != a.TaskHash ? $"task {a.Task} changed ({Short(a.TaskHash)} then, {Short(task)} now)"
        : null;

    /// <summary>The attempts a verb reads, narrowed by variants and tasks when given, split into current and stale.</summary>
    public static BankSelection Select(string root, Exam exam, IReadOnlyCollection<string>? variants = null, IReadOnlyCollection<string>? tasks = null)
    {
        var within = Of(root, exam).Where(a => (variants is null || variants.Contains(a.Variant)) && (tasks is null || tasks.Contains(a.Task))).ToList();
        return new BankSelection(within.Where(a => Stale(exam, a) is null).ToList(),
            within.Select(a => (a, Why: Stale(exam, a))).Where(x => x.Why is not null).Select(x => new StaleAttempt(x.a.Label, x.Why!)).ToList());
    }

    /// <summary>For each variant and task: the exam's attempts less its current, completed attempts in the bank.</summary>
    public static Dictionary<(string Variant, string Task), int> Gaps(Exam exam, List<BankAttempt> bank) =>
        (from variant in exam.Variants from task in exam.Tasks select (variant.Id!, task.Id!)).ToDictionary(k => k, k =>
            Math.Max(0, exam.Attempts - bank.Count(a => a.Variant == k.Item1 && a.Task == k.Item2 && a.Status == AttemptStatus.Completed && Stale(exam, a) is null)));

    /// <summary>
    /// What a new run has to make: every gap, within --variant and --tasks. Each new attempt is numbered after every attempt
    /// of its variant and task already in the bank, stale and unfinished ones included, so a variant/task/n names one attempt
    /// for good, and is its directory.
    /// </summary>
    public static List<(Variant Variant, TaskDef Task, int Attempt)> Missing(Exam exam, List<BankAttempt> bank, IReadOnlyCollection<string>? variants = null, IReadOnlyCollection<string>? tasks = null)
    {
        var gaps = Gaps(exam, bank);
        var missing = new List<(Variant, TaskDef, int)>();
        foreach (var variant in exam.Variants.Where(v => variants is null || variants.Contains(v.Id!)))
            foreach (var task in exam.Tasks.Where(t => tasks is null || tasks.Contains(t.Id!)))
            {
                var next = bank.Where(a => a.Variant == variant.Id && a.Task == task.Id).Select(a => a.Attempt).DefaultIfEmpty(0).Max() + 1;
                for (var i = 0; i < gaps[(variant.Id!, task.Id!)]; i++) missing.Add((variant, task, next + i));
            }
        return missing;
    }

    /// <summary>
    /// The runs that stopped with current attempts unfinished that the exam still lacks, oldest first, within --variant and
    /// --tasks. A run's unfinished attempts are taken up to each gap, so one whose gaps another run has since filled is
    /// not interrupted any more.
    /// </summary>
    public static List<InterruptedRun> Interrupted(Exam exam, List<BankAttempt> bank, IReadOnlyCollection<string>? variants = null, IReadOnlyCollection<string>? tasks = null)
    {
        var gaps = Gaps(exam, bank);
        var interrupted = new List<InterruptedRun>();
        foreach (var run in bank.GroupBy(a => a.Entry.Id).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var missing = run.Where(a => a.Unfinished && Stale(exam, a) is null && (variants is null || variants.Contains(a.Variant)) && (tasks is null || tasks.Contains(a.Task)))
                .GroupBy(a => (a.Variant, a.Task)).SelectMany(g => g.OrderBy(a => a.Attempt).Take(gaps[g.Key])).ToList();
            if (missing.Count == 0) continue;
            interrupted.Add(new InterruptedRun(run.First().Entry, run.Count(a => a.Status == AttemptStatus.Completed),
                run.Count(a => a.Status == AttemptStatus.Running), run.Count(a => a.Status == AttemptStatus.Failed), missing));
        }
        return interrupted;
    }

    /// <summary>The stale attempts as lines, grouped by why, each with the attempts it covers.</summary>
    public static IEnumerable<string> Describe(List<StaleAttempt> stale, string leftOut)
    {
        foreach (var why in stale.GroupBy(s => s.Why))
        {
            yield return $"  {why.Count()} stale attempt{(why.Count() == 1 ? "" : "s")} {leftOut}: {why.Key}";
            yield return $"    {string.Join(" ", why.Select(s => s.Attempt))}";
        }
    }

    public static string Short(string hash) => hash.StartsWith("sha256:") ? hash[7..15] : hash;
}
