using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Proctor;

/// <summary>
/// log/&lt;id&gt;/entry.json: what one `proctor run` did, and the conditions it ran under. The exam's hash (its definition
/// is kept once per hash under exams-seen/), each variant it made attempts of as its file was with its hash, each such
/// task's hash, and the attempts it set out to make, which is what `run` reads to continue after a crash. The attempts
/// themselves are in the bank; nothing a person reads is grouped by log entry.
/// </summary>
record LogEntry(
    string Id, string Exam, string ExamHash, List<SeenVariant> Variants, List<Ref> Tasks, List<AttemptRef> Attempts,
    string Created, string CommandLine, string Host,
    Dictionary<string, string> Versions, GitInfo? Repo, ResolvedNb Nb,
    Dictionary<string, ResolvedBundle>? Bundles = null, ResolvedHarbor? Harbor = null, List<string>? Stages = null)
{
    // Stages: a staged exam's stage ids in the order they ran, which is how its attempts' directories are named. Null for a task exam.
    public ResolvedBundle? BundleOf(string variant) => Bundles?.GetValueOrDefault(variant);
    public bool HasTask(string task) => Tasks.Any(t => t.Id == task);
}

/// <summary>A variant or a task as an attempt names it: its id and the hash that is its identity.</summary>
record Ref(string Id, string Hash);

/// <summary>One attempt a run set out to make, by its coordinates.</summary>
record AttemptRef(string Variant, string Task, int Attempt);

/// <summary>A variant as a log entry keeps it: its id, its hash and its file as it was.</summary>
record SeenVariant(string Id, string Hash, JsonObject Def);

/// <summary>exams-seen/sha256-&lt;hash&gt;.json: the exam's own definition as it stood at that hash.</summary>
record ExamSeen(string Exam, string Hash, JsonObject Def);

/// <summary>A variant's bundle as resolved for one run: where it was read from, the directory handed to the attempt, and its identity.</summary>
record ResolvedBundle(string Source, string Path, string Hash);

record GitInfo(string Commit, bool Dirty);

/// <summary>How nb is run: the host binary, its config, the runner script that stands in for the binary at the run step, if any, and where that runner shows nb the checkout and the bundle. Mounts are only in effect with a runner: a bare run resolves the host paths.</summary>
record ResolvedNb(string Path, string? Config, ResolvedRunner? Runner = null, NbMounts? Mounts = null);

/// <summary>The runner script as configured (relative to exams/), where it is, and the hash of its text.</summary>
record ResolvedRunner(string Script, string Path, string Hash);

/// <summary>An attempt's manifest.json.</summary>
record Manifest
{
    public required string Id { get; init; }
    public required string Exam { get; init; }
    /// <summary>The log entry of the run that made it.</summary>
    public required string Log { get; init; }
    public required Ref Variant { get; init; }
    public required Ref Task { get; init; }
    public required int Attempt { get; init; }
    /// <summary>The stage's place in its try, from 1, on a staged exam; then Task names the stage and Attempt the try.</summary>
    public int? Stage { get; init; }
    /// <summary>How an nb variant ran; absent on an agent variant, whose attempt is described by <see cref="Agent"/>.</summary>
    public string? Runner { get; init; }
    public string? Harness { get; init; }
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public FixtureRef? Fixture { get; init; }
    public ResolvedBundle? Bundle { get; init; }
    /// <summary>How nb was run for this attempt: the host binary, its config and the runner script, if any.</summary>
    public ResolvedNb? Nb { get; init; }
    public TranscriptRef Transcript { get; init; } = new("transcript.jsonl", "nb-jsonl");
    public string? Started { get; set; }
    public string? Ended { get; set; }
    public long? DurationMs { get; set; }
    public required string Host { get; init; }
    public required Dictionary<string, string> Versions { get; init; }
    public required string ProgramHash { get; init; }
    public string Status { get; set; } = AttemptStatus.Pending;
    public string? StatusReason { get; set; }
    public int Tries { get; set; }
    public int? NbExitCode { get; set; }
    /// <summary>An agent variant's attempt: the agent as the trial reported it, its settings, the Harbor that ran it, and what it used.</summary>
    public AgentTrial? Agent { get; set; }
    /// <summary>An oracle attempt's simulated user on the Harbor path (<see cref="Ingest"/>); absent on the bare path, where nb's oracle is in the program.</summary>
    public OracleOutcome? Oracle { get; set; }
}

record TranscriptRef(string File, string Format);

/// <summary>What an attempt was run against: the fixture and the identity of its source tree.</summary>
record FixtureRef(string Id, string Hash);

static class AttemptStatus
{
    // Nothing sets skipped yet; it stays so status.json's counts keep the key.
    public const string Pending = "pending", Running = "running", Completed = "completed", Failed = "failed", Skipped = "skipped";
    public static readonly string[] All = [Pending, Running, Completed, Failed, Skipped];
}

/// <summary>
/// How a stage ended, by its exit reason (plans/bank.md, "How a stage ends"). It ended when the agent finished its turn.
/// It did not run when the provider or the caller failed it, which says nothing about the agent and loses the try.
/// Anything else stopped it: a limit or a policy the exam set, which is a result about the agent.
/// </summary>
static class StageEnd
{
    static readonly string[] Ended = ["ok", "oracle_miss"], NotRun = ["provider_error", "rate_limited", "cancelled"];
    public static bool DidEnd(string exitReason) => Ended.Contains(exitReason);
    public static bool DidNotRun(string exitReason) => NotRun.Contains(exitReason);
}

/// <summary>One run's attempts, made into the bank: variant by variant, hooks at variant and attempt level, nb as a subprocess or an agent through Harbor.</summary>
sealed class Runner(string root, LogEntry entry, Exam exam, ProctorConfig config, TextWriter log)
{
    readonly string entryDir = Layout.LogEntry(root, entry.Id);
    readonly string examsDir = Layout.Exams(root);

    /// <summary>
    /// `proctor run`: first each interrupted run is continued, when the conditions its log entry recorded still hold; then
    /// whatever the exam still lacks, within --variant and --tasks, is made by a new run. Returns the id of the last log
    /// entry written or continued, or null when there was nothing to make.
    /// </summary>
    public static string? Run(string root, Exam exam, ProctorConfig config, string? nbOverride, string? runnerOverride, string commandLine, TextWriter log,
        IReadOnlyCollection<string>? variants = null, IReadOnlyCollection<string>? tasks = null)
    {
        RefuseChangedVariants(root, exam);
        Standing(exam, Bank.Of(root, exam), log, variants, tasks);
        string? last = null;
        var considered = new HashSet<string>();
        var continued = new HashSet<(string Entry, string Label)>();
        while (Bank.Interrupted(exam, Bank.Of(root, exam), variants, tasks).FirstOrDefault(i => !considered.Contains(i.Entry.Id)) is { } interrupted)
        {
            var entry = interrupted.Entry;
            considered.Add(entry.Id);
            log.WriteLine($"run {entry.Id} was interrupted: {interrupted.Account}");
            if (WhyNotContinue(root, entry, exam, config, runnerOverride, interrupted.Missing) is { } why)
            {
                log.WriteLine($"  not continuing it: {why}");
                continue;
            }
            log.WriteLine($"  continuing it: {AttemptCount(interrupted.Missing.Count)} to make");
            if (entry.Harbor is not null) HarborRun.Preflight(config.HarborOrDefault);
            if (nbOverride is not null) entry = entry with { Nb = entry.Nb with { Path = Path.GetFullPath(nbOverride) } };
            new Runner(root, entry, exam, config, log).Make(interrupted.Missing
                .Select(a => (exam.Variants.First(v => v.Id == a.Variant), exam.Tasks.First(t => t.Id == a.Task), a.Attempt)).ToList());
            continued.UnionWith(interrupted.Missing.Select(a => (entry.Id, a.Label)));
            last = entry.Id;
        }
        var bank = Bank.Of(root, exam);
        // What was continued just now and failed again is not made a second time by a new run: whatever stopped it is
        // still there, and the next `proctor run` continues it.
        var failedAgain = bank.Where(a => a.Unfinished && continued.Contains((a.Entry.Id, a.Label)))
            .GroupBy(a => (a.Variant, a.Task)).ToDictionary(g => g.Key, g => g.Count());
        var missing = Bank.Missing(exam, bank, variants, tasks).GroupBy(m => (m.Variant.Id!, m.Task.Id!))
            .SelectMany(g => g.SkipLast(failedAgain.GetValueOrDefault(g.Key))).ToList();
        if (failedAgain.Count > 0) log.WriteLine($"{AttemptCount(failedAgain.Values.Sum())} failed again and {(failedAgain.Values.Sum() == 1 ? "is" : "are")} left for the next `proctor run {exam.Id}`");
        if (missing.Count > 0) return Start(root, exam, config, nbOverride, runnerOverride, commandLine, log, missing);
        if (last is null)
            log.WriteLine("nothing to make");
        return last;
    }

    /// <summary>Where each variant stands before anything is made: its current, completed attempts of what the exam wants, and how many this run is to make.</summary>
    static void Standing(Exam exam, List<BankAttempt> bank, TextWriter log, IReadOnlyCollection<string>? variants, IReadOnlyCollection<string>? tasks)
    {
        var gaps = Bank.Gaps(exam, bank);
        // A staged exam is counted in tries: every stage of a variant has the same gap, since a try is all of them or none.
        var per = exam.Staged ? exam.Tasks.Count : 1;
        var wanted = exam.Attempts * exam.Tasks.Count / per;
        var width = exam.Variants.Max(v => v.Id!.Length);
        log.WriteLine(exam.Staged ? $"{exam.Id}: one job of {exam.Tasks.Count} stage{(exam.Tasks.Count == 1 ? "" : "s")}, {AttemptCount(exam.Attempts)} wanted"
            : $"{exam.Id}: {exam.Tasks.Count} task{(exam.Tasks.Count == 1 ? "" : "s")}, {AttemptCount(exam.Attempts)} wanted of each");
        foreach (var variant in exam.Variants)
        {
            var have = wanted - gaps.Where(g => g.Key.Variant == variant.Id).Sum(g => g.Value) / per;
            var making = variants is not null && !variants.Contains(variant.Id!) ? 0
                : gaps.Where(g => g.Key.Variant == variant.Id && (tasks is null || tasks.Contains(g.Key.Task))).Sum(g => g.Value) / per;
            log.WriteLine($"  {variant.Id!.PadRight(width)}  {have} of {AttemptCount(wanted)}{(making > 0 ? $"   making {making}" : have < wanted ? "   not asked for" : "")}");
        }
    }

    /// <summary>A new run that makes the given attempts: only their variants' bundles resolved, only their agent tasks compiled.</summary>
    static string Start(string root, Exam exam, ProctorConfig config, string? nbOverride, string? runnerOverride, string commandLine, TextWriter log,
        List<(Variant Variant, TaskDef Task, int Attempt)> attempts)
    {
        var variants = exam.Variants.Where(v => attempts.Any(a => a.Variant == v)).ToList();
        var tasks = exam.Tasks.Where(t => attempts.Any(a => a.Task == t)).ToList();
        var nb = ResolveNb(root, exam, config, nbOverride, runnerOverride);
        foreach (var variant in variants.Where(a => a.IsAgent)) Route(root, config, nb, variant);   // a missing endpoint fails before anything is made
        var id = Layout.NewLogEntryId(exam.Id, DateTime.UtcNow);
        var entry = new LogEntry(
            Id: id, Exam: exam.Id, ExamHash: exam.Hash,
            Variants: variants.Select(v => new SeenVariant(v.Id!, v.Hash, JsonNode.Parse(File.ReadAllText(Exam.VariantFile(exam.Dir, v.Id!)))!.AsObject())).ToList(),
            Tasks: tasks.Select(c => new Ref(c.Id!, exam.TaskHashes[c.Id!])).ToList(),
            Attempts: attempts.Select(a => new AttemptRef(a.Variant.Id!, a.Task.Id!, a.Attempt)).ToList(),
            Created: Now(), CommandLine: commandLine, Host: Environment.MachineName,
            Versions: Versions(nb.Path), Repo: GitInfo(root), Nb: nb,
            Bundles: ResolveBundles(root, variants), Stages: exam.StageIds);
        var agents = variants.Any(v => v.IsAgent);
        if (agents)
        {
            HarborRun.Preflight(config.HarborOrDefault);
            if (config.HarborOrDefault.Remote) RequireReachable(variants, tasks, exam, config, variant => Route(root, config, nb, variant));
        }
        var dir = Layout.LogEntry(root, id);
        Directory.CreateDirectory(dir);
        if (agents)
        {
            var version = exam.Def.Harbor!.Version!;
            var engine = config.HarborOrDefault.Engine;
            var nbImage = variants.Any(a => a.IsAgent && !a.IsVendor) ? NbImage(root, config, entry.Versions["nb"]) : null;
            var pairs = attempts.Where(a => a.Variant.IsAgent).Select(a => (a.Variant, a.Task)).Distinct().ToList();
            entry = entry with
            {
                Versions = new(entry.Versions) { ["harbor"] = version },
                Harbor = new ResolvedHarbor(version, HarborCompile.Tasks(dir, exam, variant => Route(root, config, nb, variant), nbImage, nbImage is not null && config.HarborOrDefault.Remote ? NbPublish(root, config) : null, entry.Bundles, pairs), nbImage, engine),
            };
        }
        KeepExam(root, exam);
        WriteJson(Path.Combine(dir, Layout.LogEntryFile), entry);
        log.WriteLine($"run {id}: {AttemptCount(exam.Staged ? attempts.Select(a => (a.Variant, a.Attempt)).Distinct().Count() : attempts.Count)} to make");
        new Runner(root, entry, exam, config, log).Make(attempts);
        return id;
    }

    /// <summary>The exam's own definition as it stands, kept under exams-seen/ once per hash, so an attempt's checks can be read after the exam has moved on.</summary>
    static void KeepExam(string root, Exam exam)
    {
        var file = Layout.ExamSeen(root, exam.Hash);
        if (File.Exists(file)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        WriteJson(file, new ExamSeen(exam.Id, exam.Hash, JsonNode.Parse(File.ReadAllText(Layout.ExamFileIn(exam.Dir)))!.AsObject()));
    }

    /// <summary>
    /// Why an interrupted run cannot be continued, or null when it can: the conditions its log entry recorded must still
    /// hold. Its runner script, its Harbor version, the Harbor tasks it compiled, its engine and its nb image; and the
    /// script checks it compiled for the attempts still to make must be the checks as they are now, or the attempts would
    /// be made with outdated verdicts.
    /// </summary>
    static string? WhyNotContinue(string root, LogEntry entry, Exam exam, ProctorConfig config, string? runnerOverride, List<BankAttempt> missing)
    {
        var runner = ResolveRunner(root, exam, runnerOverride);
        if ((runner?.Script, runner?.Hash) != (entry.Nb.Runner?.Script, entry.Nb.Runner?.Hash))
            return $"the runner is {Describe(runner)} now, {Describe(entry.Nb.Runner)} then";
        if (entry.Harbor is not { } harbor) return null;
        if (exam.Def.Harbor?.Version is { } version && version != harbor.Version)
            return $"Harbor is {version} now, {harbor.Version} then";
        var dir = Layout.LogEntry(root, entry.Id);
        if (HarborCompile.TreeHash(dir) != harbor.TasksHash)
            return $"the Harbor tasks it compiled have changed on disk ({Path.GetRelativePath(root, Path.Combine(dir, Layout.HarborDir, "tasks"))})";
        if (config.HarborOrDefault.Engine is var engine && engine != harbor.Engine)
            return $"the engine ({config.Source} harbor.engine) is {engine} now, {harbor.Engine} then; run with the --config it was made with to continue it";
        // An entry from before the layer left the local store recorded podman's tag with its localhost/ prefix; the hash is what identifies the publish.
        if (harbor.NbImage is { } image && NbImage(root, config, entry.Versions["nb"]) is var now && now.Replace("localhost/", "") != image.Replace("localhost/", ""))
            return $"nb's publish ({config.Source} harbor.nb) is {now} now, {image} then";
        var outdated = missing.Where(a => exam.Variants.First(v => v.Id == a.Variant).IsAgent).Select(a => (a.Variant, Task: exam.Tasks.First(t => t.Id == a.Task))).Distinct()
            .SelectMany(a => HarborCompile.Outdated(Layout.HarborTask(dir, a.Variant, a.Task.Id!), exam, a.Task, a.Variant).Keys).Distinct().Order(StringComparer.Ordinal).ToList();
        if (outdated.Count > 0)
            return $"the script checks it compiled are not the checks as they are now ({string.Join(", ", outdated)})";
        return null;

        static string Describe(ResolvedRunner? r) => r is null ? "none" : $"{r.Script} {r.Hash}";
    }

    static string AttemptCount(int n) => $"{n} attempt{(n == 1 ? "" : "s")}";
    public static LogEntry LoadEntry(string root, string entryId)
    {
        var file = Layout.LogEntryFileIn(root, entryId);
        if (!File.Exists(file)) throw new ProctorException($"no log entry {entryId} under {Path.Combine(root, Layout.LogDir)}");
        return ReadJson<LogEntry>(file)!;
    }

    /// <summary>
    /// A changed variant under an old id is refused: when attempts in the bank were made by a variant with this id and another
    /// hash, the variant has changed and needs a new id, or its attempts and the new ones would be compared as one. Every
    /// variant that changed is said at once, before anything is made.
    /// </summary>
    static void RefuseChangedVariants(string root, Exam exam)
    {
        var changed = new Dictionary<string, (int Attempts, SeenVariant Then, ResolvedBundle? Bundle)>();
        foreach (var entry in Bank.Entries(root, exam.Id))
            foreach (var then in entry.Variants)
            {
                if (exam.Variants.FirstOrDefault(v => v.Id == then.Id) is not { } now || now.Hash == then.Hash) continue;
                var made = entry.Attempts.Count(a => a.Variant == then.Id && File.Exists(Path.Combine(Layout.Attempt(root, exam.Id, a.Variant, a.Task, a.Attempt, entry.Stages), Layout.ManifestFile)));
                if (made == 0) continue;
                changed[then.Id] = (changed.GetValueOrDefault(then.Id).Attempts + made, then, entry.BundleOf(then.Id));   // the latest entry's record says what it was
            }
        if (changed.Count == 0) return;
        throw new ProctorException(string.Join("\n", changed.Select(c =>
        {
            var now = exam.Variants.First(v => v.Id == c.Key);
            return $"{Path.GetRelativePath(root, Exam.VariantFile(exam.Dir, c.Key))}: this is not the {c.Key} that made {c.Value.Attempts} attempt{(c.Value.Attempts == 1 ? "" : "s")}\n"
                + $"  ({Differences(root, exam, now, c.Value.Then, c.Value.Bundle)}). A changed variant is a new variant: give it a new id.";
        })));
    }

    /// <summary>What changed in a variant, field by field from the file the log entry kept, its bundle by identity; the hashes when nothing declared differs.</summary>
    static string Differences(string root, Exam exam, Variant now, SeenVariant then, ResolvedBundle? thenBundle)
    {
        var current = JsonNode.Parse(File.ReadAllText(Exam.VariantFile(exam.Dir, now.Id!)))!.AsObject();
        var differences = new List<string>();
        foreach (var field in then.Def.Select(k => k.Key).Union(current.Select(k => k.Key)).Where(k => k is not ("id" or "note" or "bundle")))
            if (!JsonNode.DeepEquals(then.Def[field], current[field]))
                differences.Add($"{field} {Text(then.Def[field])} then, {Text(current[field])} now");
        var bundleNow = Exam.BundleIdentity(root, now.Bundle);
        if ((thenBundle?.Hash ?? "") != bundleNow) differences.Add($"bundle {Short(thenBundle?.Hash)} then, {Short(bundleNow)} now");
        return differences.Count > 0 ? string.Join("; ", differences) : $"{Short(then.Hash)} then, {Short(now.Hash)} now";

        static string Text(JsonNode? node) => node is null ? "none" : node is JsonValue v && v.TryGetValue<string>(out var str) ? str : node.ToJsonString();
        static string Short(string? hash) => hash is null or "" ? "none" : hash.StartsWith("sha256:") ? hash[7..15] : hash;
    }

    /// <summary>Everything a hook or check needs to know about an attempt, for the runner and the grader alike. The log entry says how its run resolved nb, the bundles and the Harbor tasks.</summary>
    public static AttemptContext Context(string root, LogEntry entry, Exam exam, Variant variant, TaskDef task, int n) =>
        new(exam.Dir, Layout.Attempt(root, exam.Id, variant.Id!, task.Id!, n, entry.Stages), Layout.Work(root, exam.Id, variant.Id!, task.Id!, n, entry.Stages), exam.Id, variant.Id!, task, n)
        { Expect = exam.ExpectFor(task), Fixture = exam.FixtureOf(task), BundleDir = entry.BundleOf(variant.Id!)?.Path, NbRunner = entry.Nb.Runner?.Script ?? "", NbPath = entry.Nb.Path, NbConfig = entry.Nb.Config, Mounts = entry.Nb.Mounts, ScriptsInVerifier = variant.IsAgent, Vendor = variant.IsVendor,
          DiffDir = entry.Stages is null ? Layout.Attempt(root, exam.Id, variant.Id!, task.Id!, n) : Layout.Try(root, exam.Id, variant.Id!, n),
          VerifierTask = variant.IsAgent ? Layout.HarborTask(Layout.LogEntry(root, entry.Id), variant.Id!, task.Id!) : null };

    /// <summary>
    /// Each variant's bundle, once per run, for the variants it makes attempts of: a path is hashed in place; a git revision
    /// is cloned under .proctor/bundles/ and identified by its revision, and with paths only those files and directories
    /// are copied out of the clone into a directory of their own, the bundle's identity then naming them too.
    /// </summary>
    public static Dictionary<string, ResolvedBundle> ResolveBundles(string root, IEnumerable<Variant> variants)
    {
        var bundles = new Dictionary<string, ResolvedBundle>();
        foreach (var variant in variants.Where(a => a.Bundle is not null))
        {
            var source = variant.Bundle!;
            if (source.Git is { } url)
            {
                var dir = Layout.GitBundle(root, source.Rev!);
                if (!Directory.Exists(Path.Combine(dir, ".git")))
                {
                    if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
                    Directory.CreateDirectory(dir);
                    foreach (var args in new[] { new[] { "clone", "-q", url, "." }, new[] { "checkout", "-q", source.Rev! } })
                    {
                        var result = Subprocess.Run("git", args, dir);
                        if (!result.Started || result.ExitCode != 0)
                            throw new ProctorException($"bundle of variant '{variant.Id}': git {args[0]} {url} failed: {(result.Started ? result.FirstStderrLine : result.Stderr)}");
                    }
                }
                var identity = source.GitIdentity();
                if (source.Paths is { } paths) dir = Subset(root, variant, dir, paths, identity);
                bundles[variant.Id!] = new ResolvedBundle(source.Describe(), dir, identity);
            }
            else
            {
                var dir = Path.GetFullPath(Path.Combine(root, source.Path!));
                bundles[variant.Id!] = new ResolvedBundle(source.Describe(), dir, Tree.Hash(dir, new HashSet<string> { ".git" }));
            }
        }
        return bundles;
    }

    /// <summary>The paths of a git bundle, copied out of the clone once; a path the revision does not have is an error, not an empty bundle.</summary>
    static string Subset(string root, Variant variant, string clone, List<string> paths, string identity)
    {
        var dir = Layout.GitBundleSubset(root, variant.Bundle!.Rev!, Sha256(identity)["sha256:".Length..]);
        if (Directory.Exists(dir)) return dir;
        var staging = dir + ".partial";
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        foreach (var path in paths)
        {
            var from = Path.GetFullPath(Path.Combine(clone, path));
            var to = Path.Combine(staging, path);
            if (File.Exists(from)) { Directory.CreateDirectory(Path.GetDirectoryName(to)!); File.Copy(from, to, overwrite: true); }
            else if (Directory.Exists(from) && Path.GetFileName(from.TrimEnd(Path.DirectorySeparatorChar)) != ".git")
                foreach (var (relative, full) in Tree.Files(from, new HashSet<string> { ".git" }))
                {
                    var target = Path.Combine(to, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(full, target, overwrite: true);
                }
            else throw new ProctorException($"bundle of variant '{variant.Id}': {path} is not in {variant.Bundle.Git} at {variant.Bundle.Rev}");
        }
        Directory.CreateDirectory(staging);
        Directory.Move(staging, dir);
        return dir;
    }

    public static string ReadStatus(string attemptDir)
    {
        var file = Path.Combine(attemptDir, Layout.StatusFile);
        return File.Exists(file) ? File.ReadAllText(file).Trim() : AttemptStatus.Pending;
    }

    /// <summary>Make the given attempts of this run, variant by variant, the variant hooks around each variant's; then say how they came out.</summary>
    void Make(List<(Variant Variant, TaskDef Task, int Attempt)> attempts)
    {
        WriteStatusCounts();
        foreach (var group in attempts.GroupBy(a => a.Variant))
        {
            var variant = group.Key;
            var pending = group.Select(a => (a.Task, a.Attempt)).ToList();
            var hooksDir = Path.Combine(entryDir, Layout.HooksDir);
            Directory.CreateDirectory(hooksDir);
            var variantEnv = new Dictionary<string, string> { ["PROCTOR_EXAM"] = exam.Id, ["PROCTOR_VARIANT"] = variant.Id!, ["PROCTOR_EXAM_DIR"] = exam.Dir, ["PROCTOR_BUNDLE"] = entry.BundleOf(variant.Id!)?.Path ?? "", ["PROCTOR_RUNNER"] = entry.Nb.Runner?.Script ?? "" };
            var setup = RunHook(exam.Def.Hooks?.Variant?.Setup, variantEnv, Path.Combine(hooksDir, $"{variant.Id}.setup.log"));
            if (setup is not null)
            {
                foreach (var (task, n) in pending) MarkFailed(variant, task, n, $"variant setup hook failed: {setup}");
                continue;
            }

            if (exam.Staged)
                foreach (var n in pending.Select(p => p.Attempt).Distinct()) RunTry(variant, n);
            else
                foreach (var (task, n) in pending)
                    if (variant.IsAgent) RunAgentAttempt(variant, task, n);
                    else RunAttempt(variant, task, n);

            var teardown = RunHook(exam.Def.Hooks?.Variant?.Teardown, variantEnv, Path.Combine(hooksDir, $"{variant.Id}.teardown.log"));
            if (teardown is not null) log.WriteLine($"  {variant.Id}: variant teardown hook failed: {teardown}");
        }
        WriteStatusCounts();
        if (exam.Staged)
        {
            // A try is whole when every stage completed, broke when a chained one stopped part way (it still counts), and lost when a stage did not run.
            var tries = attempts.GroupBy(a => (a.Variant.Id, a.Attempt)).Select(g => g.Select(a => ReadStatus(AttemptDir(a.Variant, a.Task, a.Attempt))).ToList())
                .Select(s => s.Any(x => x is not (AttemptStatus.Completed or AttemptStatus.Skipped)) ? "lost" : s.Contains(AttemptStatus.Skipped) ? "broke" : "whole").ToList();
            log.WriteLine($"{AttemptCount(tries.Count)} made: " + string.Join(", ", new[] { "whole", "broke", "lost" }.Where(k => k == "whole" || tries.Contains(k)).Select(k => $"{tries.Count(t => t == k)} {k}")));
            return;
        }
        var statuses = attempts.Select(a => ReadStatus(AttemptDir(a.Variant, a.Task, a.Attempt))).ToList();
        var failed = statuses.Count(s => s == AttemptStatus.Failed);
        log.WriteLine($"{statuses.Count} made: {statuses.Count(s => s == AttemptStatus.Completed)} completed{(failed > 0 ? $", {failed} failed" : "")}");
    }

    string AttemptDir(Variant variant, TaskDef task, int n) => Layout.Attempt(root, exam.Id, variant.Id!, task.Id!, n, entry.Stages);

    /// <summary>
    /// One try of a staged exam: the workspace checked out and set up once, then nb started afresh for each stage in
    /// order, in that one workspace, which is never reset. Each stage keeps its own program, transcript and manifest
    /// under the try; the attempt hooks run once around all of them and the diff is the whole try's.
    /// A stage that did not run loses the try, and nothing more of it is launched. On a chained exam a stage that was
    /// stopped breaks the try: the stages after it are skipped, and the try still counts (plans/bank.md, "How a stage ends").
    /// </summary>
    void RunTry(Variant variant, int n)
    {
        var stages = exam.Tasks;
        var tryDir = Layout.Try(root, exam.Id, variant.Id!, n);
        var first = Context(root, entry, exam, variant, stages[0], n);
        var workDir = first.WorkDir;
        Directory.CreateDirectory(Path.Combine(tryDir, Layout.HooksDir));
        Directory.CreateDirectory(workDir);
        var stopwatch = Stopwatch.StartNew();

        // The try's own setup: the checkout, then the attempt hook, which sees the first stage's environment.
        var env = first.Environment();
        var lost = first.Fixture is not null && Checkout.Materialise(first.Fixture, workDir) is { } checkoutError ? $"fixture checkout failed: {checkoutError}" : null;
        var setupRan = lost is null;
        if (setupRan && RunHook(exam.Def.Hooks?.Attempt?.Setup, env, Path.Combine(tryDir, Layout.HooksDir, "attempt.setup.log")) is { } setupError)
            lost = $"attempt setup hook failed: {setupError}";
        string? lostAt = null, brokeAt = null, broke = null;
        if (lost is not null) { Mark(variant, stages[0], n, AttemptStatus.Failed, lost); lostAt = stages[0].Id; }

        foreach (var stage in stages.Skip(lostAt is null ? 0 : 1))
        {
            if (lostAt is not null) { Mark(variant, stage, n, AttemptStatus.Skipped, $"not launched: the try was lost at stage {lostAt}"); continue; }
            if (brokeAt is not null) { Mark(variant, stage, n, AttemptStatus.Skipped, $"not reached: stage {brokeAt} was stopped ({broke}), and this exam's stages are chained"); continue; }
            var (failure, exit) = RunStage(variant, stage, n);
            if (failure is not null) { lost = failure; lostAt = stage.Id; }
            else if (exam.Chained && !StageEnd.DidEnd(exit!)) { brokeAt = stage.Id; broke = exit; }
        }

        // Teardown and the diff run whenever setup ran, as for a task's attempt. Failing here loses the try: its workspace checks would have nothing to read.
        if (setupRan)
        {
            var after = RunHook(exam.Def.Hooks?.Attempt?.Teardown, env, Path.Combine(tryDir, Layout.HooksDir, "attempt.teardown.log")) is { } teardownError ? $"attempt teardown hook failed: {teardownError}"
                : first.Fixture is not null && Checkout.CollectDiff(workDir, tryDir) is { } diffError ? $"diff collection failed: {diffError}"
                : null;
            if (after is not null && lostAt is null)
            {
                var last = stages.Last(s => ReadStatus(AttemptDir(variant, s, n)) == AttemptStatus.Completed);
                Mark(variant, last, n, AttemptStatus.Failed, after);
                lost = after; lostAt = last.Id;
            }
        }
        WriteStatusCounts();
        log.WriteLine($"  {variant.Id}/{n}  {(lostAt is not null ? $"lost at {lostAt}: {lost}" : brokeAt is not null ? $"broke at {brokeAt} ({broke})" : "whole")}  {stopwatch.Elapsed.TotalSeconds:F1}s");
    }

    /// <summary>One stage of a try: nb on the stage's program, in the try's workspace. Why the try is lost, or else the stage's exit reason.</summary>
    (string? Failure, string? ExitReason) RunStage(Variant variant, TaskDef stage, int n)
    {
        var attempt = Context(root, entry, exam, variant, stage, n);
        var attemptDir = attempt.AttemptDir;
        Directory.CreateDirectory(attemptDir);
        var program = OraclePrefix(exam, stage) + ResolveProgram(exam.ProgramTemplate, variant, stage, n, attempt.WorkMount, attempt.BundleMount);
        var manifest = NewManifest(variant, stage, n, attempt.Fixture, Sha256(program));
        manifest.Tries++;
        manifest.Started = Now();
        SetStatus(attemptDir, manifest, AttemptStatus.Running);
        File.WriteAllText(Path.Combine(attemptDir, Layout.ProgramFile), program);

        var stopwatch = Stopwatch.StartNew();
        var (compiled, compileError) = Compile(attemptDir, program);
        var failure = compileError is not null ? $"program compile failed: {compileError}" : RunNb(attemptDir, attempt.WorkDir, compiled!, attempt.Environment(), manifest);
        var exit = failure is null ? ExitReasonOf(attemptDir) : null;
        if (exit is not null && StageEnd.DidNotRun(exit)) failure = $"did not run: {exit}";
        stopwatch.Stop();

        manifest.Ended = Now();
        manifest.DurationMs = stopwatch.ElapsedMilliseconds;
        manifest.StatusReason = failure;
        SetStatus(attemptDir, manifest, failure is null ? AttemptStatus.Completed : AttemptStatus.Failed);
        log.WriteLine($"  {variant.Id}/{n}/{stage.Id}  {manifest.Status}  {failure ?? exit}  {stopwatch.Elapsed.TotalSeconds:F1}s");
        return (failure, exit);
    }

    Manifest NewManifest(Variant variant, TaskDef task, int n, Fixture? fixture, string programHash) => new()
    {
        Id = Layout.NewAttemptId(), Exam = exam.Id, Log = entry.Id, Variant = RefOf(variant), Task = RefOf(task), Attempt = n,
        Stage = entry.Stages is { } stages ? stages.IndexOf(task.Id!) + 1 : null,
        Runner = variant.Runner, Harness = variant.Harness, Provider = variant.Provider, Model = variant.Model,
        Fixture = fixture is null ? null : new FixtureRef(fixture.Id, fixture.Hash),
        Bundle = entry.BundleOf(variant.Id!), Nb = entry.Nb,
        Host = entry.Host, Versions = entry.Versions, ProgramHash = programHash,
    };

    /// <summary>A stage that was not launched, or one whose try failed around it: its manifest and status, with why.</summary>
    void Mark(Variant variant, TaskDef stage, int n, string status, string reason)
    {
        var attemptDir = AttemptDir(variant, stage, n);
        Directory.CreateDirectory(attemptDir);
        var manifestFile = Path.Combine(attemptDir, Layout.ManifestFile);
        var manifest = File.Exists(manifestFile) ? ReadManifest(manifestFile) : NewManifest(variant, stage, n, exam.FixtureOf(stage), "");
        manifest.StatusReason = reason;
        SetStatus(attemptDir, manifest, status);
        log.WriteLine($"  {variant.Id}/{n}/{stage.Id}  {status}  {reason}");
    }

    void RunAttempt(Variant variant, TaskDef task, int n)
    {
        var attempt = Context(root, entry, exam, variant, task, n);
        var (attemptDir, workDir) = (attempt.AttemptDir, attempt.WorkDir);
        Directory.CreateDirectory(Path.Combine(attemptDir, Layout.HooksDir));
        Directory.CreateDirectory(workDir);

        var program = OraclePrefix(exam, task) + ResolveProgram(exam.ProgramTemplate, variant, task, n, attempt.WorkMount, attempt.BundleMount);
        var manifestFile = Path.Combine(attemptDir, Layout.ManifestFile);
        var manifest = File.Exists(manifestFile)
            ? ReadManifest(manifestFile)
            : new Manifest
            {
                Id = Layout.NewAttemptId(), Exam = exam.Id, Log = entry.Id, Variant = RefOf(variant), Task = RefOf(task), Attempt = n,
                Runner = variant.Runner, Harness = variant.Harness, Provider = variant.Provider, Model = variant.Model,
                Fixture = attempt.Fixture is null ? null : new FixtureRef(attempt.Fixture.Id, attempt.Fixture.Hash),
                Bundle = entry.BundleOf(variant.Id!), Nb = entry.Nb,
                Host = entry.Host, Versions = entry.Versions, ProgramHash = Sha256(program),
            };
        manifest.Tries++;
        manifest.Started = Now();
        manifest.Ended = null;
        manifest.DurationMs = null;
        manifest.StatusReason = null;
        SetStatus(attemptDir, manifest, AttemptStatus.Running);
        File.WriteAllText(Path.Combine(attemptDir, Layout.ProgramFile), program);

        var stopwatch = Stopwatch.StartNew();
        var env = attempt.Environment();
        // The program is compiled on the host first, then the fixture is checked out, then the setup hook, nb, the teardown hook, and the diff is collected.
        var (compiled, compileError) = Compile(attemptDir, program);
        var failure = compileError is not null ? $"program compile failed: {compileError}"
            : attempt.Fixture is not null && Checkout.Materialise(attempt.Fixture, workDir) is { } checkoutError ? $"fixture checkout failed: {checkoutError}"
            : null;
        var setupRan = failure is null;
        if (setupRan)
            failure = RunHook(exam.Def.Hooks?.Attempt?.Setup, env, Path.Combine(attemptDir, Layout.HooksDir, "attempt.setup.log")) is { } setupError
                ? $"attempt setup hook failed: {setupError}"
                : RunNb(attemptDir, workDir, compiled!, env, manifest);
        // Teardown and the diff run whenever setup ran, even when setup failed: a container it half-made must not be left for the attempt made again.
        if (setupRan)
        {
            if (RunHook(exam.Def.Hooks?.Attempt?.Teardown, env, Path.Combine(attemptDir, Layout.HooksDir, "attempt.teardown.log")) is { } teardownError)
                failure ??= $"attempt teardown hook failed: {teardownError}";
            if (attempt.Fixture is not null && Checkout.CollectDiff(workDir, attemptDir) is { } diffError)
                failure ??= $"diff collection failed: {diffError}";
        }
        stopwatch.Stop();

        manifest.Ended = Now();
        manifest.DurationMs = stopwatch.ElapsedMilliseconds;
        manifest.StatusReason = failure;
        SetStatus(attemptDir, manifest, failure is null ? AttemptStatus.Completed : AttemptStatus.Failed);
        WriteStatusCounts();
        log.WriteLine($"  {variant.Id}/{task.Id}/{n}  {manifest.Status}  {(failure ?? ExitReasonOf(attemptDir))}  {stopwatch.Elapsed.TotalSeconds:F1}s");
    }

    /// <summary>
    /// An agent variant's attempt: one Harbor trial of the task compiled for the variant, then ingest. The checkout, the setup and
    /// the diff are the image's and Harbor's; variant hooks still ran on the host around the variant's attempts. The variant's bundle is
    /// the host's too: recorded here and handed to those hooks, never put in the container.
    /// </summary>
    void RunAgentAttempt(Variant variant, TaskDef task, int n)
    {
        var attemptDir = Layout.Attempt(root, exam.Id, variant.Id!, task.Id!, n);
        Directory.CreateDirectory(attemptDir);
        var taskDir = Layout.HarborTask(entryDir, variant.Id!, task.Id!);
        var route = Route(root, config, entry.Nb, variant);
        var fixture = exam.FixtureOf(task)!;
        // nb's program is compiled on the host as on the bare path, with the checkout where the image has it.
        var program = variant.IsVendor ? null : ResolveProgram(exam.ProgramTemplate, variant, task, n, HarborCompile.Workdir);
        var manifestFile = Path.Combine(attemptDir, Layout.ManifestFile);
        var manifest = File.Exists(manifestFile)
            ? ReadManifest(manifestFile)
            : new Manifest
            {
                Id = Layout.NewAttemptId(), Exam = exam.Id, Log = entry.Id, Variant = RefOf(variant), Task = RefOf(task), Attempt = n, Model = variant.Model,
                Harness = variant.Harness, Provider = variant.Provider,
                Fixture = new FixtureRef(fixture.Id, fixture.Hash), Bundle = entry.BundleOf(variant.Id!), Transcript = new TranscriptRef(Layout.TrajectoryFile, "atif"),
                Host = entry.Host, Versions = entry.Versions,
                ProgramHash = Sha256(program ?? File.ReadAllText(Path.Combine(taskDir, "instruction.md"))),
            };
        var nb = program is null ? null : new NbAttempt(Path.Combine(attemptDir, Layout.CompiledProgramFile), entry.Nb.Config);
        var oracle = OracleAttemptOf(variant, task);
        manifest.Agent = new AgentTrial(variant.Agent!, null, variant.Model ?? "", HarborCompile.AgentKwargs(variant, route, nb), HarborCompile.AgentEnv(variant, route), entry.Harbor!.Version, null);
        manifest.Oracle = null;
        manifest.Tries++;
        manifest.Started = Now();
        manifest.Ended = null;
        manifest.DurationMs = null;
        manifest.StatusReason = null;
        SetStatus(attemptDir, manifest, AttemptStatus.Running);

        var stopwatch = Stopwatch.StartNew();
        string? trialDir = null, failure = null;
        if (program is not null)
        {
            File.WriteAllText(Path.Combine(attemptDir, Layout.ProgramFile), program);
            var (compiled, compileError) = Compile(attemptDir, program);
            if (compileError is not null) failure = $"program compile failed: {compileError}";
            else if (oracle is not null) File.WriteAllText(nb!.Program, HarborCompile.WithoutRun(compiled!));
        }
        if (failure is null)
            (trialDir, failure) = HarborRun.Trial(config.Harbor?.Uv ?? new HarborConfig().Uv, entry.Harbor.Version, entry.Harbor.Engine, attemptDir, variant, taskDir, route, exam.Def.Limits!.WallMs!.Value, HarborCompile.AgentsDir(entryDir), nb, oracle);
        if (trialDir is not null) failure = Ingest.Trial(trialDir, attemptDir, variant, manifest, variant.IsVendor ? variant.AgentVersion! : entry.Versions["nb"], bridged: oracle is not null);
        stopwatch.Stop();

        manifest.Ended = Now();
        manifest.DurationMs = stopwatch.ElapsedMilliseconds;
        manifest.StatusReason = failure;
        SetStatus(attemptDir, manifest, failure is null ? AttemptStatus.Completed : AttemptStatus.Failed);
        WriteStatusCounts();
        var exitReason = failure is null ? ReadJson<CountedCore>(Path.Combine(attemptDir, Layout.CoreFile))?.ExitReason : null;
        log.WriteLine($"  {variant.Id}/{task.Id}/{n}  {manifest.Status}  {failure ?? exitReason}  {stopwatch.Elapsed.TotalSeconds:F1}s");
    }

    /// <summary>
    /// An oracle task's simulated user on this variant: the host nb judges, with its config, as the variant's own provider unless
    /// the exam names a judge, which is what nb's in-process oracle does on the bare path.
    /// </summary>
    OracleAttempt? OracleAttemptOf(Variant variant, TaskDef task) => exam.OracleOf(task) is { } sheet
        ? new OracleAttempt(sheet, entry.Nb.Path, entry.Nb.Config, exam.Def.Oracle?.Judge ?? variant.Provider, exam.OracleTurns,
            Path.Combine(HarborCompile.AgentsDir(entryDir), HarborCompile.OracleTemplateFile))
        : null;

    /// <summary>
    /// Where an agent variant's model calls go, ${VAR} resolved now: the key never reaches a file. A vendor agent's is
    /// proctor.json's agents block; nb's is its own config's provider entry, whose endpoint variables go to the agent's
    /// process by value and whose key goes by name, as on the bare path, where nb resolves them itself. A provider
    /// with no endpoint (Mock) reaches nothing.
    /// </summary>
    static AgentRoute Route(string root, ProctorConfig config, ResolvedNb nb, Variant variant)
    {
        if (!variant.IsVendor) return NbRoute(nb, variant);
        var endpoint = config.Agents?.GetValueOrDefault(variant.Agent!)
            ?? throw new ProctorException($"{config.Source} has no agents.{variant.Agent} (variant '{variant.Id}'): add {{\"base_url\", \"api_key\"}} for it, as in proctor.example.json");
        var baseUrl = ResolveEnv(endpoint.BaseUrl!, $"{config.Source} agents.{variant.Agent}.base_url").TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _)) throw new ProctorException($"{config.Source} agents.{variant.Agent}.base_url: '{baseUrl}' is not a URL");
        var agentConfig = endpoint.Config is null ? null : Path.GetFullPath(Path.Combine(Layout.Exams(root), endpoint.Config));
        if (agentConfig is not null && !File.Exists(agentConfig)) throw new ProctorException($"{config.Source} agents.{variant.Agent}.config: {agentConfig} not found");
        return new AgentRoute(baseUrl, endpoint.ApiKey is null ? null : ResolveEnv(endpoint.ApiKey, $"{config.Source} agents.{variant.Agent}.api_key"), HarborCompile.KeyEnv(variant.Agent!), Config: agentConfig);
    }

    /// <summary>
    /// On a remote engine, every agent variant must reach its model from the internet, and no task may have a service beside
    /// it: Harbor's Modal environment runs a compose task as docker-in-docker with no allowlist, and refuses it rather
    /// than run it unconfined. Everything wrong is said at once, before anything is made.
    /// </summary>
    static void RequireReachable(List<Variant> variants, List<TaskDef> tasks, Exam exam, ProctorConfig config, Func<Variant, AgentRoute> route)
    {
        var engine = config.HarborOrDefault.Engine;
        var problems = new List<string>();
        foreach (var variant in variants.Where(a => a.IsAgent))
            if (route(variant).Host is { } host && HarborRun.Unreachable(host) is { } why)
                problems.Add($"variant '{variant.Id}': its model is at {host}, {why}, which a sandbox on {engine} cannot reach; run it on a local engine, or point {(variant.IsVendor ? $"{config.Source} agents.{variant.Agent}" : $"nb's config {variant.Provider}")} at a public endpoint");
        foreach (var fixture in tasks.Select(exam.FixtureOf).OfType<Fixture>().DistinctBy(f => f.Id))
            if (fixture.Def.Image?.Services is not null)
                problems.Add($"fixture '{fixture.Id}' runs services beside the agent (image.services), and {engine} cannot confine the network of a task with services; its tasks run on docker or podman only");
        if (problems.Count > 0) throw new ProctorException(string.Join("\n", problems));
    }

    static readonly System.Text.RegularExpressions.Regex EnvRef = new(@"\$\{([A-Za-z_][A-Za-z0-9_]*)\}");

    static AgentRoute NbRoute(ResolvedNb nb, Variant variant)
    {
        var where = nb.Config ?? "nb's config";
        var providers = nb.Config is null ? null : JsonNode.Parse(File.ReadAllText(nb.Config), documentOptions: new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })?["ChatProviders"] as JsonArray;
        var entry = providers?.OfType<JsonObject>().FirstOrDefault(p => p["Name"]?.GetValue<string>() == variant.Provider)
            ?? throw new ProctorException($"variant '{variant.Id}': provider '{variant.Provider}' is not in {where} ChatProviders; an nb variant in Harbor reads its endpoint from there");
        var endpoint = entry["Endpoint"]?.GetValue<string>();
        if (endpoint is null) return new AgentRoute(null, null);
        var env = EnvRef.Matches(endpoint).Select(m => m.Groups[1].Value).Distinct().ToDictionary(v => v, v => ResolveEnv($"${{{v}}}", $"{where} {variant.Provider}.Endpoint"));
        var baseUrl = EnvRef.Replace(endpoint, m => env[m.Groups[1].Value]);
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _)) throw new ProctorException($"{where} {variant.Provider}.Endpoint: '{baseUrl}' is not a URL");
        var key = entry["ApiKey"]?.GetValue<string>() is { } k && EnvRef.Match(k) is { Success: true } m ? m.Groups[1].Value : null;
        return new AgentRoute(baseUrl, key is null ? null : ResolveEnv($"${{{key}}}", $"{where} {variant.Provider}.ApiKey"), key, env);
    }

    /// <summary>nb's layer from proctor.json harbor.nb, which must be the same nb as the host binary that compiles the programs and runs the bare variants.</summary>
    static string NbImage(string root, ProctorConfig config, string hostVersion)
    {
        var publish = config.Harbor?.Nb ?? throw new ProctorException($"an nb variant runs through Harbor on nb's self-contained linux-x64 publish: set harbor.nb in {config.Source} to its directory");
        var (tag, version) = HarborRun.NbImage(NbPublish(root, config), config.HarborOrDefault);
        if (version != hostVersion)
            throw new ProctorException($"nb's publish ({config.Source} harbor.nb) is {version}, the host nb is {hostVersion}: publish the same nb, so the program compiled on the host is the program the image runs");
        return tag;
    }

    /// <summary>The directory of nb's publish, proctor.json harbor.nb resolved against exams/; compile copies it into each nb task's image on a remote engine.</summary>
    static string NbPublish(string root, ProctorConfig config) => Path.GetFullPath(Path.Combine(Layout.Exams(root), config.Harbor!.Nb!));

    static string ResolveEnv(string value, string field) =>
        System.Text.RegularExpressions.Regex.Replace(value, @"\$\{([A-Za-z_][A-Za-z0-9_]*)\}", m => Environment.GetEnvironmentVariable(m.Groups[1].Value)
            ?? throw new ProctorException($"{m.Groups[1].Value} is not set in the environment or {EnvFile.DefaultPath()} ({field})"));

    /// <summary>
    /// `nb --compile` on the host binary, in the exam directory so `@file` includes resolve against the exam: the
    /// JSONL that goes down stdin, with every include inlined, so nothing nb runs on names a file the runner would
    /// have to carry. Written beside the source as program.jsonl. The program hash stays the source's. A program
    /// nb refuses fails here, on the host, before a checkout or a container exists for it.
    /// </summary>
    (string? Compiled, string? Error) Compile(string attemptDir, string program)
    {
        var nb = entry.Nb;
        var args = new List<string> { "--compile" };
        if (nb.Config is not null) args.AddRange(["--config", nb.Config]);
        var result = Subprocess.Run(nb.Path, args, exam.Dir, new Dictionary<string, string> { ["NO_COLOR"] = "1" }, stdin: program);
        if (!result.Started) return (null, result.Stderr);
        if (result.ExitCode != 0) return (null, $"nb --compile exited {result.ExitCode}: {result.FirstStderrLine}");
        File.WriteAllText(Path.Combine(attemptDir, Layout.CompiledProgramFile), result.Stdout);
        return (result.Stdout, null);
    }

    /// <summary>
    /// Run nb on the compiled program, which travels on stdin. Bare, that is nb itself with the argv the runner
    /// contract names; with a runner, it is the script with nothing on argv and the attempt environment, which must
    /// start nb the same way wherever it runs. Null on success; otherwise why the attempt is `failed` (infrastructure only).
    /// </summary>
    string? RunNb(string attemptDir, string workDir, string program, IDictionary<string, string> attemptEnv, Manifest manifest)
    {
        var nb = entry.Nb;
        var args = new List<string> { "--output", "jsonl" };
        if (nb.Config is not null) args.AddRange(["--config", nb.Config]);
        args.Add("-");
        var env = nb.Runner is null
            ? new Dictionary<string, string> { ["NO_COLOR"] = "1" }
            : new Dictionary<string, string>(attemptEnv) { ["NO_COLOR"] = "1" };
        // nb's own ATIF beside its JSONL, by environment so a runner script passes it on untouched. Grading reads the
        // trajectory (Transcript.Read), as on the Harbor path; the JSONL stays the record nb streamed.
        env["NB_TRAJECTORY"] = Path.GetFullPath(Path.Combine(attemptDir, Layout.TrajectoryFile));
        var result = Subprocess.Run(nb.Runner?.Path ?? nb.Path, nb.Runner is null ? args : [], workDir, env,
            stdoutFile: Path.Combine(attemptDir, Layout.TranscriptFile),
            stderrFile: Path.Combine(attemptDir, Layout.StderrFile),
            stdin: program);
        if (!result.Started) return result.Stderr;
        manifest.NbExitCode = result.ExitCode;
        var trailer = Transcript.Read(attemptDir).Trailer;
        if (trailer is null)
        {
            var stderr = File.ReadAllLines(Path.Combine(attemptDir, Layout.StderrFile)).Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
            return $"nb exited {result.ExitCode} without a result trailer: {stderr}";
        }
        return null;
    }

    /// <summary>Run a hook script if one is declared. Null on success, else a one-line reason. Output goes to the log file.</summary>
    string? RunHook(string? script, IDictionary<string, string> env, string logFile)
    {
        if (script is null) return null;
        var result = Subprocess.Run(Path.GetFullPath(Path.Combine(exam.Dir, script)), [], exam.Dir, env);
        File.WriteAllText(logFile, result.Stdout + (result.Stderr.Length > 0 ? "\n--- stderr ---\n" + result.Stderr : ""));
        if (!result.Started) return result.Stderr;
        return result.ExitCode == 0 ? null : $"{script} exited {result.ExitCode}: {result.FirstStderrLine}";
    }

    void MarkFailed(Variant variant, TaskDef task, int n, string reason)
    {
        var attemptDir = AttemptDir(variant, task, n);
        Directory.CreateDirectory(attemptDir);
        var manifestFile = Path.Combine(attemptDir, Layout.ManifestFile);
        var manifest = File.Exists(manifestFile)
            ? ReadManifest(manifestFile)
            : new Manifest
            {
                Id = Layout.NewAttemptId(), Exam = exam.Id, Log = entry.Id, Variant = RefOf(variant), Task = RefOf(task), Attempt = n,
                Runner = variant.Runner, Harness = variant.Harness, Provider = variant.Provider, Model = variant.Model,
                Host = entry.Host, Versions = entry.Versions, ProgramHash = "",
            };
        manifest.Tries++;
        manifest.StatusReason = reason;
        SetStatus(attemptDir, manifest, AttemptStatus.Failed);
        log.WriteLine($"  {variant.Id}/{task.Id}/{n}  failed  {reason}");
    }

    Ref RefOf(Variant variant) => new(variant.Id!, variant.Hash);
    Ref RefOf(TaskDef task) => new(task.Id!, exam.TaskHashes[task.Id!]);

    static void SetStatus(string attemptDir, Manifest manifest, string status)
    {
        manifest.Status = status;
        File.WriteAllText(Path.Combine(attemptDir, Layout.StatusFile), status + "\n");
        WriteJson(Path.Combine(attemptDir, Layout.ManifestFile), manifest);
    }

    void WriteStatusCounts()
    {
        var counts = AttemptStatus.All.ToDictionary(s => s, _ => 0);
        foreach (var a in entry.Attempts) counts[ReadStatus(Layout.Attempt(root, exam.Id, a.Variant, a.Task, a.Attempt, entry.Stages))]++;
        WriteJson(Path.Combine(entryDir, Layout.StatusCountsFile), new { planned = entry.Attempts.Count, counts, updated = Now() });
    }

    static string? ExitReasonOf(string attemptDir)
    {
        var file = Path.Combine(attemptDir, Layout.TranscriptFile);
        return File.Exists(file) ? Transcript.Read(attemptDir).Trailer?.ExitReason : null;
    }

    /// <summary>
    /// A bare attempt's oracle, as nb's own directives ahead of the template: they apply to every run after them, so the
    /// template needs no placeholder. The sheet is the host path, which --compile inlines, so a runner's container never
    /// needs the file. Empty for a task without a sheet.
    /// </summary>
    public static string OraclePrefix(Exam exam, TaskDef task)
    {
        if (exam.OracleOf(task) is not { } sheet) return "";
        var prefix = $"oracle @{sheet}\n";
        if (exam.Def.Oracle?.Judge is { } judge) prefix += $"oracle provider {judge}\n";
        return prefix + $"budget oracle_turns {exam.OracleTurns}\n";
    }

    /// <summary>Fill the template. {{work}} and {{bundle}} are the paths the model will see, which a runner may mount elsewhere. A prompt's newlines become nb continuation lines so the program stays one directive.</summary>
    public static string ResolveProgram(string template, Variant variant, TaskDef c, int n, string workDir, string? bundleDir = null)
    {
        var values = new Dictionary<string, string>
        {
            ["prompt"] = c.Prompt!.Replace("\r\n", "\n").Replace("\n", " \\\n"),
            ["task"] = c.Id!,
            ["work"] = workDir,
            ["bundle"] = bundleDir ?? "",
            ["provider"] = variant.Provider ?? "",
            ["model"] = variant.Model ?? "",
            ["harness"] = variant.Harness ?? "",
            ["variant"] = variant.Id!,
            ["attempt"] = n.ToString(),
        };
        return System.Text.RegularExpressions.Regex.Replace(template, @"\{\{\s*([^}]*?)\s*\}\}", m => values[m.Groups[1].Value]);
    }

    static ResolvedNb ResolveNb(string root, Exam exam, ProctorConfig config, string? nbOverride, string? runnerOverride)
    {
        var nb = config.NbOrDefault;
        var examsDir = Layout.Exams(root);
        if (!File.Exists(Path.Combine(root, config.Source)) && File.Exists(Path.Combine(examsDir, "proctor.example.json")))
            throw new ProctorException("no exams/proctor.json: run `proctor setup` at a terminal, which writes it (skip every job for a config the playground runs on); or copy exams/proctor.example.json and nb.example.json beside themselves and point them at your gateway");
        var path = nbOverride ?? nb.Path;
        if (path.Contains(Path.DirectorySeparatorChar)) path = Path.GetFullPath(Path.Combine(nbOverride is null ? examsDir : Directory.GetCurrentDirectory(), path));
        else if (FindOnPath(path) is { } found) path = found;
        else throw new ProctorException($"nb not found: '{path}' is not on PATH; set nb.path in {config.Source} or pass --nb");
        if (!File.Exists(path)) throw new ProctorException($"nb not found at {path}");
        var configPath = nb.Config is null ? null : Path.GetFullPath(Path.Combine(examsDir, nb.Config));
        if (configPath is not null && !File.Exists(configPath)) throw new ProctorException($"nb config not found at {configPath} ({config.Source} nb.config){(File.Exists(Path.ChangeExtension(configPath, ".example.json")) ? $"; copy {Path.GetFileName(Path.ChangeExtension(configPath, ".example.json"))} beside it" : "")}");
        var runner = ResolveRunner(root, exam, runnerOverride);
        return new ResolvedNb(path, configPath, runner, runner is null ? null : exam.Def.Nb?.Mounts);
    }

    /// <summary>The runner in effect: --runner (from the current directory) over the exam's nb.runner (from the exam directory); `--runner none` is a bare run. Recorded relative to exams/ when it lives there, else absolute, so continuing the run can compare it.</summary>
    static ResolvedRunner? ResolveRunner(string root, Exam exam, string? runnerOverride)
    {
        var examsDir = Layout.Exams(root);
        var script = runnerOverride ?? exam.Def.Nb?.Runner;
        if (script is null || runnerOverride == "none") return null;
        var path = Path.GetFullPath(Path.Combine(runnerOverride is null ? exam.Dir : Directory.GetCurrentDirectory(), script));
        if (!File.Exists(path)) throw new ProctorException($"runner not found at {path} ({(runnerOverride is null ? $"exams/{exam.Id}/exam.json nb.runner" : "--runner")})");
        var relative = Path.GetRelativePath(examsDir, path);
        return new ResolvedRunner(relative.StartsWith("..") ? path : relative, path, Sha256(File.ReadAllText(path)));
    }

    /// <summary>The first <paramref name="name"/> on PATH; on Windows also with each of PATHEXT's extensions (<c>uv.exe</c> for <c>uv</c>).</summary>
    internal static string? FindOnPath(string name)
    {
        var names = OperatingSystem.IsWindows() && !Path.HasExtension(name)
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries).Select(e => name + e).Prepend(name)
            : [name];
        return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(d => names.Select(n => Path.Combine(d, n))).FirstOrDefault(File.Exists);
    }

    /// <summary>Proctor's own version, from the csproj, without the +commit suffix; what --version prints and every log entry records.</summary>
    public static string ProctorVersion =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0";

    static Dictionary<string, string> Versions(string nbPath)
    {
        var proctor = ProctorVersion;
        var result = Subprocess.Run(nbPath, ["--version"], Path.GetDirectoryName(nbPath)!, new Dictionary<string, string> { ["NO_COLOR"] = "1" });
        var nb = result.Started && result.ExitCode == 0 ? result.Stdout.Trim().Split('+')[0] : "";
        return new() { ["proctor"] = proctor, ["nb"] = nb.Length > 0 ? nb : "unknown" };
    }

    static GitInfo? GitInfo(string root)
    {
        var head = Subprocess.Run("git", ["rev-parse", "HEAD"], root);
        if (!head.Started || head.ExitCode != 0) return null;
        var status = Subprocess.Run("git", ["status", "--porcelain"], root);
        return new GitInfo(head.Stdout.Trim(), status.Stdout.Trim().Length > 0);
    }

    static string Now() => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

    static string Sha256(string text) =>
        "sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));

    public static void WriteJson(string file, object value) =>
        File.WriteAllText(file, JsonSerializer.Serialize(value, value.GetType(), Exam.JsonOptions) + "\n");

    public static Manifest ReadManifest(string file) => ReadJson<Manifest>(file)!;

    public static T? ReadJson<T>(string file) where T : class =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(file), Exam.JsonOptions);
}
