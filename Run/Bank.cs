using System.Text.Json.Nodes;

namespace Proctor;

/// <summary>
/// One stage of one try in an exam's bank: the configuration by its key, the stage, the try's number among the
/// configuration's, its status, the configuration and stage hashes it was made under, and the log entry of the run that
/// made it, which says how nb and Harbor were resolved for it.
/// </summary>
record BankAttempt(LogEntry Entry, string Variant, string Task, int Attempt, string Status, string VariantHash, string TaskHash)
{
    public string Label => $"{Variant}/{Attempt}/{Task}";
    /// <summary>The id the try is kept under.</summary>
    public string Try => Entry.IdOf(Variant, Attempt)!;
    public string Dir(string root) => Layout.Attempt(root, Entry.Exam, Task, Entry.Stages!, Try);
    /// <summary>Never finished: pending, running when the run stopped, or failed for a reason of the machine's. A completed stage is a result, whatever its exit reason.</summary>
    public bool Unfinished => Status is AttemptStatus.Pending or AttemptStatus.Running or AttemptStatus.Failed;
    /// <summary>
    /// A stage of a lost try: some stage of it did not run (plans/bank.md, "How a stage ends"), so none of it counts, the
    /// stages that did finish included. It stays on disk; nothing reads it as a result and nothing takes it up again.
    /// </summary>
    public bool Lost { get; init; }
    /// <summary>
    /// Whether it fills what the exam wants: its try is whole, every stage completed, or skipped because a chained try
    /// broke before it, which is a result too.
    /// </summary>
    public bool Counts => !Lost;
}

/// <summary>An attempt left out because a hash it was made under has moved: which, and how.</summary>
record StaleAttempt(string Attempt, string Why);

/// <summary>What a verb reads: the current stages of the tries within the configurations asked for, and the stale ones it leaves out.</summary>
record BankSelection(List<BankAttempt> Current, List<StaleAttempt> Stale);

/// <summary>
/// An exam's bank is every try of it under bank/&lt;exam&gt;/, whenever it was made. A try is current when the
/// configuration and stage hashes it was made under are today's, and stale otherwise; only current tries count, and
/// only whole ones fill what the exam wants. A stale try stays where it is and is left out.
/// </summary>
static class Bank
{
    /// <summary>The log entries of an exam's runs, oldest first (an entry's id starts with its time). Not those of the earlier shape, whose attempts nothing reads (<see cref="Earlier"/>).</summary>
    public static List<LogEntry> Entries(string root, string examId) => AllEntries(root, examId).Where(e => !e.Earlier).ToList();

    /// <summary>
    /// How many attempts the exam's runs made under tasks and variants, before it was stages and axes. They stay in the
    /// bank where they were made (bank/&lt;exam&gt;/&lt;variant&gt;/…) and are read by nothing: what was under test and
    /// what it met are named and hashed another way now, so they are no configuration's tries.
    /// </summary>
    public static int Earlier(string root, string examId) => AllEntries(root, examId).Where(e => e.Earlier).Sum(e => e.Attempts.Count);

    static List<LogEntry> AllEntries(string root, string examId)
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

    /// <summary>The keys of the configurations every run of the exam made tries of. Read from the log entries as written, since the exam is not loaded yet; a name that is no key (an entry of the earlier shape) is left out by whoever reads these.</summary>
    public static List<string> Keys(string root, string examId)
    {
        var logDir = Path.Combine(root, Layout.LogDir);
        if (!Directory.Exists(logDir)) return [];
        var keys = new List<string>();
        foreach (var dir in Directory.GetDirectories(logDir).Order(StringComparer.Ordinal))
        {
            var file = Path.Combine(dir, Layout.LogEntryFile);
            if (!File.Exists(file)) continue;
            JsonNode? entry;
            try { entry = JsonNode.Parse(File.ReadAllText(file)); }
            catch (System.Text.Json.JsonException) { continue; }
            if (entry?["exam"]?.GetValue<string>() != examId) continue;
            foreach (var variant in entry["variants"] as JsonArray ?? [])
                if (variant?["id"]?.GetValue<string>() is { } id && !keys.Contains(id)) keys.Add(id);
        }
        return keys;
    }

    /// <summary>Every stage of every try each run of the exam set out to make. One never started has no manifest, and was to be made under the hashes its log entry recorded.</summary>
    public static List<BankAttempt> Of(string root, Exam exam)
    {
        var all = Read(root, exam);
        var lost = all.Where(a => a.Unfinished).Select(a => (a.Variant, a.Attempt)).ToHashSet();
        return all.Select(a => lost.Contains((a.Variant, a.Attempt)) ? a with { Lost = true } : a).ToList();
    }

    static List<BankAttempt> Read(string root, Exam exam) =>
        Entries(root, exam.Id).SelectMany(entry => entry.Attempts.Select(a =>
        {
            var dir = Layout.Attempt(root, exam.Id, a.Task, entry.Stages!, a.Id!);
            var manifestFile = Path.Combine(dir, Layout.ManifestFile);
            var manifest = File.Exists(manifestFile) ? Runner.ReadManifest(manifestFile) : null;
            return new BankAttempt(entry, a.Variant, a.Task, a.Attempt, Runner.ReadStatus(dir),
                manifest?.Variant.Hash ?? entry.Variants.First(v => v.Id == a.Variant).Hash,
                manifest?.Task.Hash ?? entry.Tasks.First(t => t.Id == a.Task).Hash);
        })).ToList();

    /// <summary>Null when the stage is current; otherwise which hash moved since its try was made.</summary>
    public static string? Stale(Exam exam, BankAttempt a) =>
        exam.Variants.FirstOrDefault(v => v.Id == a.Variant) is not { } variant ? $"configuration {a.Variant} is not declared now"
        : variant.Hash != a.VariantHash ? $"configuration {a.Variant} changed ({Short(a.VariantHash)} then, {Short(variant.Hash)} now)"
        : !exam.TaskHashes.TryGetValue(a.Task, out var task) ? $"stage {a.Task} is no longer in the exam"
        : task != a.TaskHash ? $"the stages changed ({Short(a.TaskHash)} then, {Short(task)} now)"
        : null;

    /// <summary>What a verb reads, narrowed to the configurations given, by key, split into current and stale.</summary>
    public static BankSelection Select(string root, Exam exam, IReadOnlyCollection<string>? variants = null)
    {
        var within = Of(root, exam).Where(a => variants is null || variants.Contains(a.Variant)).ToList();
        // A lost try's stages are neither current nor stale: they are not results, and nothing moved.
        return new BankSelection(within.Where(a => Stale(exam, a) is null && !a.Lost).ToList(),
            within.Select(a => (a, Why: Stale(exam, a))).Where(x => x.Why is not null).Select(x => new StaleAttempt(x.a.Label, x.Why!)).ToList());
    }

    /// <summary>For each configuration and stage: the tries the exam wants less its current, whole tries in the bank.</summary>
    public static Dictionary<(string Variant, string Task), int> Gaps(Exam exam, List<BankAttempt> bank) =>
        (from variant in exam.Variants from task in exam.StagesOf(variant.Id!) select (variant.Id!, task.Id!)).ToDictionary(k => k, k =>
            Math.Max(0, exam.Attempts - bank.Count(a => a.Variant == k.Item1 && a.Task == k.Item2 && a.Counts && Stale(exam, a) is null)));

    /// <summary>
    /// What a new run has to make: every gap, within the configurations given. Each new try is numbered after every try
    /// of its configuration already in the bank, stale and lost ones included, so a configuration's nth try names one
    /// try for good. In the order they are made: each configuration's tries in order, each try's stages in theirs.
    /// </summary>
    public static List<(Variant Variant, TaskDef Task, int Attempt)> Missing(Exam exam, List<BankAttempt> bank, IReadOnlyCollection<string>? variants = null)
    {
        var gaps = Gaps(exam, bank);
        var missing = new List<(Variant, TaskDef, int)>();
        foreach (var variant in exam.Variants.Where(v => variants is null || variants.Contains(v.Id!)))
            foreach (var task in exam.StagesOf(variant.Id!))
            {
                var next = bank.Where(a => a.Variant == variant.Id && a.Task == task.Id).Select(a => a.Attempt).DefaultIfEmpty(0).Max() + 1;
                for (var i = 0; i < gaps[(variant.Id!, task.Id!)]; i++) missing.Add((variant, task, next + i));
            }
        return missing.GroupBy(m => m.Item1).SelectMany(g => g.OrderBy(m => m.Item3)).ToList();
    }

    /// <summary>The stale stages as lines, grouped by why, each with the stages it covers.</summary>
    public static IEnumerable<string> Describe(List<StaleAttempt> stale, string leftOut)
    {
        foreach (var why in stale.GroupBy(s => s.Why))
        {
            yield return $"  {why.Count()} stale stage{(why.Count() == 1 ? "" : "s")} {leftOut}: {why.Key}";
            yield return $"    {string.Join(" ", why.Select(s => s.Attempt))}";
        }
    }

    public static string Short(string hash) => hash.StartsWith("sha256:") ? hash[7..15] : hash;
}
