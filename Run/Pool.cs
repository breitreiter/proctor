using System.Text.Json.Nodes;

namespace Proctor;

/// <summary>An attempt of an exam, whichever batch made it: its coordinates, its status, and the variant and task hashes it was made under.</summary>
record PoolAttempt(Batch Batch, string Variant, string Task, int Attempt, string Status, string VariantHash, string TaskHash)
{
    public string Label => $"{Variant}/{Task}/{Attempt}";
    public string Dir(string root) => Layout.Attempt(Layout.Batch(root, Batch.Id), Variant, Task, Attempt);
    /// <summary>Never finished: pending, running when the batch stopped, or failed for a reason of the machine's. A completed attempt is a result, whatever its exit reason.</summary>
    public bool Unfinished => Status is AttemptStatus.Pending or AttemptStatus.Running or AttemptStatus.Failed;
}

/// <summary>An attempt left out because a hash it was made under has moved: which, and how.</summary>
record StaleAttempt(string Batch, string Attempt, string Why);

/// <summary>What a verb reads: the current attempts within --batch, --variant and --tasks, and the stale ones it leaves out.</summary>
record PoolSelection(List<PoolAttempt> Current, List<StaleAttempt> Stale);

/// <summary>A batch that stopped before it finished: its counts, and its unfinished current attempts that are still missing, which `run` continues.</summary>
record InterruptedBatch(Batch Batch, int Finished, int Running, int Failed, List<PoolAttempt> Missing)
{
    public string Account => $"{Finished} of {Batch.Attempts.Count} attempt{(Batch.Attempts.Count == 1 ? "" : "s")} finished"
        + (Running > 0 ? $", {Running} {(Running == 1 ? "was" : "were")} running" : "")
        + (Failed > 0 ? $", {Failed} failed" : "");
}

/// <summary>
/// An exam's attempts are every attempt of every batch of it under attempts/. An attempt is current when the variant and
/// task hashes it was made under are today's, and stale otherwise; only current attempts count, and only current,
/// completed ones fill the exam's attempts.
/// </summary>
static class Pool
{
    /// <summary>The batches of an exam, oldest first (a batch id starts with its time).</summary>
    public static List<Batch> Batches(string root, string examId)
    {
        var attemptsDir = Path.Combine(root, Layout.AttemptsDir);
        if (!Directory.Exists(attemptsDir)) return [];
        var batches = new List<Batch>();
        foreach (var dir in Directory.GetDirectories(attemptsDir).Order(StringComparer.Ordinal))
        {
            var file = Path.Combine(dir, Layout.BatchFile);
            if (!File.Exists(file) || JsonNode.Parse(File.ReadAllText(file))?["exam"]?.GetValue<string>() != examId) continue;
            batches.Add(Runner.LoadBatch(root, Path.GetFileName(dir)));
        }
        return batches;
    }

    /// <summary>Every attempt each batch of the exam set out to make. One never started has no manifest, and was to be made under the hashes its batch recorded.</summary>
    public static List<PoolAttempt> Of(string root, Exam exam) =>
        Batches(root, exam.Id).SelectMany(batch => batch.Attempts.Select(a =>
        {
            var dir = Layout.Attempt(Layout.Batch(root, batch.Id), a.Variant, a.Task, a.Attempt);
            var manifestFile = Path.Combine(dir, Layout.ManifestFile);
            var manifest = File.Exists(manifestFile) ? Runner.ReadManifest(manifestFile) : null;
            return new PoolAttempt(batch, a.Variant, a.Task, a.Attempt, Runner.ReadStatus(dir),
                manifest?.Variant.Hash ?? batch.Variants.First(v => v.Id == a.Variant).Hash,
                manifest?.Task.Hash ?? batch.Tasks.First(t => t.Id == a.Task).Hash);
        })).ToList();

    /// <summary>Null when the attempt is current; otherwise which hash moved since it was made.</summary>
    public static string? Stale(Exam exam, PoolAttempt a) =>
        exam.Variants.FirstOrDefault(v => v.Id == a.Variant) is not { } variant ? $"variant {a.Variant} has no file now"
        : variant.Hash != a.VariantHash ? $"variant {a.Variant} changed ({Short(a.VariantHash)} then, {Short(variant.Hash)} now)"
        : !exam.TaskHashes.TryGetValue(a.Task, out var task) ? $"task {a.Task} is no longer in the exam"
        : task != a.TaskHash ? $"task {a.Task} changed ({Short(a.TaskHash)} then, {Short(task)} now)"
        : null;

    /// <summary>The attempts a verb reads, narrowed by batch, variants and tasks when given, split into current and stale.</summary>
    public static PoolSelection Select(string root, Exam exam, string? batch = null, IReadOnlyCollection<string>? variants = null, IReadOnlyCollection<string>? tasks = null)
    {
        var pool = Of(root, exam);
        if (batch is not null && pool.All(a => a.Batch.Id != batch))
            throw new ProctorException($"no batch {batch} of exam '{exam.Id}' under {Layout.AttemptsDir}/ ({string.Join(", ", pool.Select(a => a.Batch.Id).Distinct().DefaultIfEmpty("none"))})");
        var within = pool.Where(a => (batch is null || a.Batch.Id == batch) && (variants is null || variants.Contains(a.Variant)) && (tasks is null || tasks.Contains(a.Task))).ToList();
        return new PoolSelection(within.Where(a => Stale(exam, a) is null).ToList(),
            within.Select(a => (a, Why: Stale(exam, a))).Where(x => x.Why is not null).Select(x => new StaleAttempt(x.a.Batch.Id, x.a.Label, x.Why!)).ToList());
    }

    /// <summary>For each variant and task: the exam's attempts less its current, completed attempts in the pool.</summary>
    static Dictionary<(string Variant, string Task), int> Gaps(Exam exam, List<PoolAttempt> pool) =>
        (from variant in exam.Variants from task in exam.Tasks select (variant.Id!, task.Id!)).ToDictionary(k => k, k =>
            Math.Max(0, exam.Attempts - pool.Count(a => a.Variant == k.Item1 && a.Task == k.Item2 && a.Status == AttemptStatus.Completed && Stale(exam, a) is null)));

    /// <summary>
    /// What a new batch has to make: every gap, within --variant and --tasks. Each new attempt is numbered after every attempt
    /// of its variant and task already on disk, stale and unfinished ones included, so a variant/task/n names one attempt
    /// across the pool.
    /// </summary>
    public static List<(Variant Variant, TaskDef Task, int Attempt)> Missing(Exam exam, List<PoolAttempt> pool, IReadOnlyCollection<string>? variants = null, IReadOnlyCollection<string>? tasks = null)
    {
        var gaps = Gaps(exam, pool);
        var missing = new List<(Variant, TaskDef, int)>();
        foreach (var variant in exam.Variants.Where(v => variants is null || variants.Contains(v.Id!)))
            foreach (var task in exam.Tasks.Where(t => tasks is null || tasks.Contains(t.Id!)))
            {
                var next = pool.Where(a => a.Variant == variant.Id && a.Task == task.Id).Select(a => a.Attempt).DefaultIfEmpty(0).Max() + 1;
                for (var i = 0; i < gaps[(variant.Id!, task.Id!)]; i++) missing.Add((variant, task, next + i));
            }
        return missing;
    }

    /// <summary>
    /// The batches that stopped with current attempts unfinished that the exam still lacks, oldest first, within --variant and
    /// --tasks. A batch's unfinished attempts are taken up to each gap, so one whose gaps another batch has since filled is
    /// not interrupted any more.
    /// </summary>
    public static List<InterruptedBatch> Interrupted(Exam exam, List<PoolAttempt> pool, IReadOnlyCollection<string>? variants = null, IReadOnlyCollection<string>? tasks = null)
    {
        var gaps = Gaps(exam, pool);
        var interrupted = new List<InterruptedBatch>();
        foreach (var batch in pool.GroupBy(a => a.Batch.Id).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var missing = batch.Where(a => a.Unfinished && Stale(exam, a) is null && (variants is null || variants.Contains(a.Variant)) && (tasks is null || tasks.Contains(a.Task)))
                .GroupBy(a => (a.Variant, a.Task)).SelectMany(g => g.OrderBy(a => a.Attempt).Take(gaps[g.Key])).ToList();
            if (missing.Count == 0) continue;
            interrupted.Add(new InterruptedBatch(batch.First().Batch, batch.Count(a => a.Status == AttemptStatus.Completed),
                batch.Count(a => a.Status == AttemptStatus.Running), batch.Count(a => a.Status == AttemptStatus.Failed), missing));
        }
        return interrupted;
    }

    /// <summary>The stale attempts as lines, grouped by why, each with the attempts it covers by batch.</summary>
    public static IEnumerable<string> Describe(List<StaleAttempt> stale, string leftOut)
    {
        foreach (var why in stale.GroupBy(s => s.Why))
        {
            yield return $"  {why.Count()} stale attempt{(why.Count() == 1 ? "" : "s")} {leftOut}: {why.Key}";
            foreach (var batch in why.GroupBy(s => s.Batch))
                yield return $"    {batch.Key}: {string.Join(" ", batch.Select(s => s.Attempt))}";
        }
    }

    public static string Short(string hash) => hash.StartsWith("sha256:") ? hash[7..15] : hash;
}
