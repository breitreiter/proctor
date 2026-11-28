using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Auspex;

/// <summary>
/// log/&lt;id&gt;/entry.json: what one `proctor run` did, and the conditions it ran under. The exam's hash (its definition
/// is kept once per hash under exams-seen/), each configuration it made tries of (its key, its hash and each axis's
/// value by name and hash), the stages' hash, and every stage of every try it set out to make. The tries themselves
/// are in the bank; nothing a person reads is grouped by log entry. Stages is the stage ids in the order they ran,
/// which is how a try's stage directories are named. An entry written before an exam was stages and axes has no Stages,
/// or tries with no id: it is of the earlier shape (<see cref="Earlier"/>), and nothing reads its attempts.
/// </summary>
record LogEntry(
    string Id, string Exam, string ExamHash, List<SeenConfiguration> Configurations, List<Ref> StageHashes, List<AttemptRef> Attempts,
    string Created, string CommandLine, string Host,
    Dictionary<string, string> Versions, GitInfo? Repo, ResolvedNb Nb,
    ResolvedHarbor? Harbor = null, List<string>? Stages = null)
{
    /// <summary>The id a try's directory is named by.</summary>
    public string? IdOf(string key, int attempt) => Attempts.FirstOrDefault(a => a.Configuration == key && a.Attempt == attempt)?.Id;
    /// <summary>Written by an earlier proctor: by a run of an exam of tasks and variants, laid out by variant and number where a try is now kept under an id of its own, or before an entry named its configurations and stages.</summary>
    public bool Earlier => Configurations is null || StageHashes is null || Stages is null || Attempts.Any(a => a.Id is null || a.Configuration is null);
}

/// <summary>A configuration, a value or a stage as a manifest names it: its id and the hash that is its identity.</summary>
record Ref(string Id, string Hash);

/// <summary>One stage of one try a run set out to make: the configuration's key, the stage, the try's number among the configuration's, and the id the try is kept under.</summary>
record AttemptRef(string Configuration, string Stage, int Attempt, string? Id = null);

/// <summary>A configuration as a log entry keeps it: its key, its hash, and each axis's value by name and hash.</summary>
record SeenConfiguration(string Id, string Hash, JsonObject Def);

/// <summary>exams-seen/sha256-&lt;hash&gt;.json: the exam's own definition as it stood at that hash.</summary>
record ExamSeen(string Exam, string Hash, JsonObject Def);

record GitInfo(string Commit, bool Dirty);

/// <summary>How nb is run: the host binary, its config, the runner script that stands in for the binary at the run step, if any, and where that runner shows nb the checkout. Mounts are only in effect with a runner: a bare run resolves the host paths.</summary>
record ResolvedNb(string Path, string? Config, ResolvedRunner? Runner = null, NbMounts? Mounts = null);

/// <summary>The runner script as configured (relative to exams/), where it is, and the hash of its text.</summary>
record ResolvedRunner(string Script, string Path, string Hash);

/// <summary>manifest.json of one stage of one try.</summary>
record Manifest
{
    public required string Id { get; init; }
    public required string Exam { get; init; }
    /// <summary>The log entry of the run that made it.</summary>
    public required string Log { get; init; }
    /// <summary>The configuration, by its key and the hash that is its identity.</summary>
    public required Ref Configuration { get; init; }
    public required Ref Stage { get; init; }
    public required int Attempt { get; init; }
    /// <summary>The stage's place in its try, from 1. Stage names the stage, Configuration the configuration by its key, and Attempt the try by its number among the configuration's.</summary>
    public int? Place { get; init; }
    /// <summary>The try's id, which is its directory; the value on each axis, by name and hash; and a hash of the workspace as the stage left it, kept whether or not the workspace is.</summary>
    public string? Try { get; init; }
    public Dictionary<string, Ref>? Values { get; init; }
    public string? WorkspaceHash { get; set; }
    /// <summary>The workspace the try started from, after the values' files were laid and the setup hook ran: the tree of that commit.</summary>
    public string? StartTree { get; set; }
    /// <summary>How nb ran, bare; absent through Harbor, where the stage is described by <see cref="Agent"/>.</summary>
    public string? Runner { get; init; }
    public string? Harness { get; init; }
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public FixtureRef? Fixture { get; init; }
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
    /// <summary>Through Harbor: the agent as the trial reported it, its settings, the Harbor that ran it, and what it used.</summary>
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

/// <summary>One run's tries, made into the bank: configuration by configuration, hooks around each configuration's tries and around each try, nb as a subprocess or an agent through Harbor.</summary>
sealed class Runner(string root, LogEntry entry, Exam exam, AuspexConfig config, TextWriter log)
{
    readonly string entryDir = Layout.LogEntry(root, entry.Id);
    readonly string examsDir = Layout.Exams(root);

    /// <summary>
    /// `proctor run`: whatever the exam still lacks, within the configurations given, is made by a new run. A try that
    /// was lost or never finished is not taken up again part way, since its workspace is half way through: it is made
    /// again, whole, under the next number. Returns the id of the log entry written, or null when there was nothing to make.
    /// </summary>
    public static string? Run(string root, Exam exam, AuspexConfig config, string? nbOverride, string? runnerOverride, string commandLine, TextWriter log,
        IReadOnlyCollection<string>? keys = null)
    {
        RefuseChangedValues(root, exam);
        var bank = Bank.Of(root, exam);
        Standing(exam, bank, log, keys);
        var missing = Bank.Missing(exam, bank, keys);
        if (missing.Count > 0) return Start(root, exam, config, nbOverride, runnerOverride, commandLine, log, missing);
        log.WriteLine("nothing to make");
        return null;
    }

    /// <summary>Where each configuration stands before anything is made: its current tries that count, of those the exam wants, and how many this run is to make.</summary>
    static void Standing(Exam exam, List<BankAttempt> bank, TextWriter log, IReadOnlyCollection<string>? keys)
    {
        var gaps = Bank.Gaps(exam, bank);
        // Counted in tries: every stage of a configuration has the same gap, since a try is all of them or none.
        int Per(Assistant v) => Math.Max(1, exam.StagesOf(v.Id!).Count);
        var width = exam.Assistants.Max(v => v.Id!.Length);
        log.WriteLine($"{exam.Id}: one job of {exam.Stages.Count} stage{(exam.Stages.Count == 1 ? "" : "s")}, {AttemptCount(exam.Attempts)} wanted");
        // Two configurations are compared try against try, and under 4 tries a side nothing can be confirmed (3 against 5 can).
        if (exam.Assistants.Count > 1 && Tries.MinP(exam.Attempts, exam.Attempts) >= 0.05)
            log.WriteLine($"  note: {AttemptCount(exam.Attempts)} a configuration can show a difference between two and never confirm one; 4 a configuration can");
        foreach (var assistant in exam.Assistants)
        {
            var (per, wanted) = (Per(assistant), exam.Attempts);
            var have = wanted - gaps.Where(g => g.Key.Configuration == assistant.Id).Sum(g => g.Value) / per;
            var making = keys is not null && !keys.Contains(assistant.Id!) ? 0 : gaps.Where(g => g.Key.Configuration == assistant.Id).Sum(g => g.Value) / per;
            log.WriteLine($"  {assistant.Id!.PadRight(width)}  {have} of {AttemptCount(wanted)}{(making > 0 ? $"   making {making}" : have < wanted ? "   not asked for" : "")}");
        }
    }

    /// <summary>A new run that makes the given tries, stage by stage: only what goes through Harbor among them is compiled.</summary>
    static string Start(string root, Exam exam, AuspexConfig config, string? nbOverride, string? runnerOverride, string commandLine, TextWriter log,
        List<(Assistant Assistant, StageDef Stage, int Attempt)> attempts)
    {
        var assistants = exam.Assistants.Where(v => attempts.Any(a => a.Assistant == v)).ToList();
        var tasks = exam.Stages.Where(t => attempts.Any(a => a.Stage == t)).ToList();
        var nb = ResolveNb(root, exam, config, nbOverride, runnerOverride);
        foreach (var assistant in assistants.Where(a => a.IsAgent)) Route(root, config, nb, assistant);   // a missing endpoint fails before anything is made
        var id = Layout.NewLogEntryId(exam.Id, DateTime.UtcNow);
        // A try is named when it is made, by time and chance, and never by its place among the others.
        var tryIds = new Dictionary<(string, int), string>();
        foreach (var key in attempts.Select(a => (a.Assistant.Id!, a.Attempt)).Distinct())
        {
            string tryId;
            do tryId = Layout.NewTryId(DateTime.UtcNow); while (tryIds.ContainsValue(tryId) || Directory.Exists(Layout.Try(root, exam.Id, tryId)));
            tryIds[key] = tryId;
        }
        var entry = new LogEntry(
            Id: id, Exam: exam.Id, ExamHash: exam.Hash,
            Configurations: assistants.Select(v => new SeenConfiguration(v.Id!, v.Hash, SeenDef(exam, v))).ToList(),
            StageHashes: tasks.Select(c => new Ref(c.Id!, exam.StageHashes[c.Id!])).ToList(),
            Attempts: attempts.Select(a => new AttemptRef(a.Assistant.Id!, a.Stage.Id!, a.Attempt, tryIds[(a.Assistant.Id!, a.Attempt)])).ToList(),
            Created: Now(), CommandLine: commandLine, Host: Environment.MachineName,
            Versions: Versions(nb.Path), Repo: GitInfo(root), Nb: nb,
            Stages: exam.StageIds);
        var agents = assistants.Any(v => v.IsAgent);
        ResolvedRelay? relay = null;
        if (agents)
        {
            HarborRun.Preflight(config.HarborOrDefault);
            var made = attempts.Where(a => a.Assistant.IsAgent).Select(a => (a.Assistant, a.Stage)).Distinct().ToList();
            relay = Relay(id, exam, made, assistant => Route(root, config, nb, assistant));
            foreach (var host in made.SelectMany(p => HarborCompile.Hosts(Route(root, config, nb, p.Assistant), exam.FixtureOf(p.Stage)!)).Distinct().Where(h => !HostRelay.Loopback(h)))
                if (HarborRun.SubnetWarning(config.HarborOrDefault.Engine, host, System.Net.Dns.GetHostAddresses) is { } warning) log.WriteLine($"warning: {warning}");
        }
        var dir = Layout.LogEntry(root, id);
        Directory.CreateDirectory(dir);
        if (agents)
        {
            var version = exam.Def.Harbor!.Version!;
            var engine = config.HarborOrDefault.Engine;
            var nbImage = assistants.Any(a => a.IsAgent && !a.IsVendor) ? NbImage(root, config, entry.Versions["nb"]) : null;
            var pairs = attempts.Where(a => a.Assistant.IsAgent).Select(a => (a.Assistant, a.Stage)).Distinct().ToList();
            entry = entry with
            {
                Versions = new(entry.Versions) { ["harbor"] = version },
                Harbor = new ResolvedHarbor(version, HarborCompile.Tasks(dir, exam, assistant => Route(root, config, nb, assistant), nbImage, pairs, relay?.Dir), nbImage, engine, relay),
            };
        }
        KeepExam(root, exam);
        WriteJson(Path.Combine(dir, Layout.LogEntryFile), entry);
        log.WriteLine($"run {id}: {AttemptCount(attempts.Select(a => (a.Assistant, a.Attempt)).Distinct().Count())} to make");
        new Runner(root, entry, exam, config, log).Make(attempts);
        return id;
    }

    /// <summary>What a log entry keeps of a configuration: each axis's value, by name and hash.</summary>
    static JsonObject SeenDef(Exam exam, Assistant v) => JsonSerializer.SerializeToNode(exam.Axes.Seen(exam.Axes.Of(v.Id!)!), Exam.JsonOptions)!.AsObject();

    /// <summary>The exam's own definition as it stands, kept under exams-seen/ once per hash, so an attempt's checks can be read after the exam has moved on.</summary>
    static void KeepExam(string root, Exam exam)
    {
        var file = Layout.ExamSeen(root, exam.Hash);
        if (File.Exists(file)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        WriteJson(file, new ExamSeen(exam.Id, exam.Hash, JsonNode.Parse(File.ReadAllText(Layout.ExamFileIn(exam.Dir)))!.AsObject()));
    }

    static string AttemptCount(int n) => $"{n} attempt{(n == 1 ? "" : "s")}";
    public static LogEntry LoadEntry(string root, string entryId)
    {
        var file = Layout.LogEntryFileIn(root, entryId);
        if (!File.Exists(file)) throw new AuspexException($"no log entry {entryId} under {Path.Combine(root, Layout.LogDir)}");
        return ReadJson<LogEntry>(file)!;
    }

    /// <summary>
    /// A value's name stands for its content: one whose content changed under the name it made tries by is refused, or
    /// those tries and the new ones would be compared as one. Every value that changed is said at once, before anything is made.
    /// </summary>
    static void RefuseChangedValues(string root, Exam exam)
    {
        var axes = exam.Axes;
        var changed = new Dictionary<string, SeenConfiguration>();
        foreach (var entry in Bank.Entries(root, exam.Id))
            foreach (var then in entry.Configurations)
            {
                if (exam.Assistants.FirstOrDefault(v => v.Id == then.Id) is not { } now || now.Hash == then.Hash) continue;
                if (!entry.Attempts.Any(a => a.Configuration == then.Id && File.Exists(Path.Combine(Layout.Attempt(root, exam.Id, a.Stage, entry.Stages!, a.Id!), Layout.ManifestFile)))) continue;
                changed[then.Id] = then;   // the latest entry's record says what it was
            }
        if (changed.Count == 0) return;
        throw new AuspexException(string.Join("\n", changed.SelectMany(c => axes.Seen(axes.Of(c.Key)!)
            .Where(now => c.Value.Def[now.Key]?["hash"]?.GetValue<string>() is { } then && then != now.Value.Hash)
            .Select(now => (Axis: now.Key, Value: now.Value.Id)))
            .GroupBy(x => (x.Axis, x.Value)).Select(g => $"exams/{exam.Id}/exam.json: axes.{g.Key.Axis}.values.{g.Key.Value}: this is not the {g.Key.Axis}={g.Key.Value} that made attempts ({string.Join(", ", changed.Keys.Where(k => axes.Of(k)![g.Key.Axis] == g.Key.Value))}).\n"
                + "  A changed value is a new value: give it a new name, and keep the old one as it was.")));
    }

    /// <summary>Everything a hook or check needs to know about one stage of a try, for the runner and the grader alike. The log entry says how its run resolved nb and the Harbor tasks.</summary>
    public static AttemptContext Context(string root, LogEntry entry, Exam exam, Assistant assistant, StageDef task, int n)
    {
        var (configuration, tryId) = (exam.Axes.Of(assistant.Id!)!, entry.IdOf(assistant.Id!, n)!);
        return new(exam.Dir, Layout.Attempt(root, exam.Id, task.Id!, entry.Stages!, tryId), Layout.Work(root, exam.Id, tryId), exam.Id, assistant.Id!, task, n)
        {
            Axis = configuration.Values.ToDictionary(v => v.Axis, v => v.Value),
            Params = exam.Axes.Params(configuration, exam.Def, exam.FixtureOf(task), exam.Dir),
            Overlays = exam.Axes.Overlays(configuration, exam.Dir),
            Try = tryId,
            Expect = exam.ExpectFor(task), Fixture = exam.FixtureOf(task), NbRunner = entry.Nb.Runner?.Script ?? "", NbPath = entry.Nb.Path, NbConfig = entry.Nb.Config, Mounts = entry.Nb.Mounts,
            ScriptsInVerifier = assistant.IsAgent, Vendor = assistant.IsVendor,
            VerifierTask = assistant.IsAgent ? Layout.HarborJobTask(Layout.LogEntry(root, entry.Id), assistant.Id!, exam.Id) : null,
        };
    }

    public static string ReadStatus(string attemptDir)
    {
        var file = Path.Combine(attemptDir, Layout.StatusFile);
        return File.Exists(file) ? File.ReadAllText(file).Trim() : AttemptStatus.Pending;
    }

    /// <summary>Make the given tries of this run, configuration by configuration, the configuration hooks around each one's tries; then say how they came out.</summary>
    void Make(List<(Assistant Assistant, StageDef Stage, int Attempt)> attempts)
    {
        // The relay's host end lives as long as the tries are being made, at the directory the log entry recorded.
        using var relay = entry.Harbor?.Relay is { } r && attempts.Any(a => a.Assistant.IsAgent) ? HostRelay.Start(r.Dir, r.Ports) : null;
        WriteStatusCounts();
        foreach (var group in attempts.GroupBy(a => a.Assistant))
        {
            var assistant = group.Key;
            var pending = group.Select(a => (a.Stage, a.Attempt)).ToList();
            var hooksDir = Path.Combine(entryDir, Layout.HooksDir);
            Directory.CreateDirectory(hooksDir);
            var hookEnv = new Dictionary<string, string> { ["PROCTOR_EXAM"] = exam.Id, ["PROCTOR_CONFIGURATION"] = assistant.Id!, ["PROCTOR_EXAM_DIR"] = exam.Dir, ["PROCTOR_RUNNER"] = entry.Nb.Runner?.Script ?? "" };
            foreach (var (axis, value) in exam.Axes.Of(assistant.Id!)!.Values) hookEnv["PROCTOR_AXIS_" + axis.ToUpperInvariant().Replace('-', '_')] = value;
            var setup = RunHook(exam.Def.Hooks?.Configuration?.Setup, hookEnv, Path.Combine(hooksDir, $"{assistant.Id}.setup.log"));
            if (setup is not null)
            {
                foreach (var (task, n) in pending) Mark(assistant, task, n, AttemptStatus.Failed, $"configuration setup hook failed: {setup}");
                continue;
            }

            foreach (var n in pending.Select(p => p.Attempt).Distinct())
                if (assistant.IsAgent) RunAgentTry(assistant, n);
                else RunTry(assistant, n);

            var teardown = RunHook(exam.Def.Hooks?.Configuration?.Teardown, hookEnv, Path.Combine(hooksDir, $"{assistant.Id}.teardown.log"));
            if (teardown is not null) log.WriteLine($"  {assistant.Id}: configuration teardown hook failed: {teardown}");
        }
        WriteStatusCounts();
        // A try is whole when every stage completed, broke when a chained one stopped part way (it still counts), and lost when a stage did not run.
        var tries = attempts.GroupBy(a => (a.Assistant.Id, a.Attempt)).Select(g => g.Select(a => ReadStatus(AttemptDir(a.Assistant, a.Stage, a.Attempt))).ToList())
            .Select(s => s.Any(x => x is not (AttemptStatus.Completed or AttemptStatus.Skipped)) ? "lost" : s.Contains(AttemptStatus.Skipped) ? "broke" : "whole").ToList();
        log.WriteLine($"{AttemptCount(tries.Count)} made: " + string.Join(", ", new[] { "whole", "broke", "lost" }.Where(k => k == "whole" || tries.Contains(k)).Select(k => $"{tries.Count(t => t == k)} {k}")));
    }

    string AttemptDir(Assistant assistant, StageDef task, int n) => Layout.Attempt(root, exam.Id, task.Id!, entry.Stages!, entry.IdOf(assistant.Id!, n)!);
    string TryDir(Assistant assistant, int n) => Layout.Try(root, exam.Id, entry.IdOf(assistant.Id!, n)!);

    /// <summary>
    /// One try, bare: the workspace checked out and set up once, then nb started afresh for each stage in
    /// order. Each stage starts from the workspace as the try began, unless the exam is chained, when it meets what the
    /// stages before it left. Each stage keeps its own program, transcript, manifest and diff under the try: the diff is
    /// collected as the stage ends, so a check sees the workspace as that stage left it. The attempt hooks run once
    /// around all of them.
    /// A stage that did not run loses the try, and nothing more of it is launched. On a chained exam a stage that was
    /// stopped breaks the try: the stages after it are skipped, and the try still counts (plans/bank.md, "How a stage ends").
    /// </summary>
    string? startTree;

    void RunTry(Assistant assistant, int n)
    {
        var stages = exam.StagesOf(assistant.Id!);
        var tryDir = TryDir(assistant, n);
        var first = Context(root, entry, exam, assistant, stages[0], n);
        var workDir = first.WorkDir;
        Directory.CreateDirectory(Path.Combine(tryDir, Layout.HooksDir));
        Directory.CreateDirectory(workDir);
        var stopwatch = Stopwatch.StartNew();

        // The try's own setup: the checkout, then the attempt hook, which sees the first stage's environment.
        var env = first.Environment();
        var lost = first.Fixture is not null && Checkout.Materialise(first.Fixture, workDir, first.Overlays) is { } checkoutError ? $"fixture checkout failed: {checkoutError}" : null;
        var setupRan = lost is null;
        if (setupRan && RunHook(exam.Def.Hooks?.Try?.Setup, env, Path.Combine(tryDir, Layout.HooksDir, "try.setup.log")) is { } setupError)
            lost = $"try setup hook failed: {setupError}";
        // The starting state is what setup left, committed: a stage's diff is then the agent's alone (plans/axes-build.md, 3b).
        startTree = null;
        if (lost is null && first.Fixture is not null && Checkout.CommitSetup(workDir, Path.Combine(tryDir, Layout.SetupPatchFile), out startTree) is { } commitError)
            lost = $"could not record the starting state: {commitError}";
        string? lostAt = null, brokeAt = null, broke = null;
        if (lost is not null) { Mark(assistant, stages[0], n, AttemptStatus.Failed, lost); lostAt = stages[0].Id; }

        foreach (var stage in stages.Skip(lostAt is null ? 0 : 1))
        {
            if (lostAt is not null) { Mark(assistant, stage, n, AttemptStatus.Skipped, $"not launched: the try was lost at stage {lostAt}"); continue; }
            if (brokeAt is not null) { Mark(assistant, stage, n, AttemptStatus.Skipped, $"not reached: stage {brokeAt} was stopped ({broke}), and this exam's stages are chained"); continue; }
            var fresh = !exam.Chained && stage != stages[0] && first.Fixture is not null ? Checkout.Reset(workDir) : null;
            if (fresh is not null) { lost = $"could not restore the workspace: {fresh}"; lostAt = stage.Id; Mark(assistant, stage, n, AttemptStatus.Failed, lost); continue; }
            var (failure, exit) = RunStage(assistant, stage, n, collectDiff: first.Fixture is not null);
            if (failure is not null) { lost = failure; lostAt = stage.Id; }
            else if (exam.Chained && !StageEnd.DidEnd(exit!)) { brokeAt = stage.Id; broke = exit; }
        }

        // Teardown runs whenever setup ran, even when setup failed, and a failure there loses the try.
        if (setupRan)
        {
            var after = RunHook(exam.Def.Hooks?.Try?.Teardown, env, Path.Combine(tryDir, Layout.HooksDir, "try.teardown.log")) is { } teardownError ? $"try teardown hook failed: {teardownError}" : null;
            if (after is not null && lostAt is null)
            {
                var last = stages.Last(s => ReadStatus(AttemptDir(assistant, s, n)) == AttemptStatus.Completed);
                Mark(assistant, last, n, AttemptStatus.Failed, after);
                lost = after; lostAt = last.Id;
            }
        }
        WriteStatusCounts();
        log.WriteLine($"  {assistant.Id}/{n}  {(lostAt is not null ? $"lost at {lostAt}: {lost}" : brokeAt is not null ? $"broke at {brokeAt} ({broke})" : "whole")}  {stopwatch.Elapsed.TotalSeconds:F1}s");
    }

    /// <summary>
    /// One try through Harbor: one Harbor trial of the configuration's compiled task, whose steps are the
    /// stages, then each step ingested as its stage's attempt. Harbor runs every step it can: it goes on after a step
    /// that timed out, and ends the trial only at a step that failed outright. So the endings are read afterwards, and
    /// they come out as on the bare path: a stage that did not run loses the try, and on a chained exam the stages after
    /// a stopped one are not reached. Those stages did run in the container; what they did is left in the trial and
    /// read by nothing, so the two runners grade a try the same way.
    /// </summary>
    void RunAgentTry(Assistant assistant, int n)
    {
        var stages = exam.StagesOf(assistant.Id!);
        var tryDir = TryDir(assistant, n);
        Directory.CreateDirectory(tryDir);
        var taskDir = Layout.HarborJobTask(entryDir, assistant.Id!, exam.Id);
        // One stage with an answer sheet is a plain task: the simulated user answers a task, never a step.
        if (HarborCompile.Plain(exam, assistant.Id!)) { RunAgentTask(assistant, stages[0], n, AttemptDir(assistant, stages[0], n), taskDir); return; }
        var route = Route(root, config, entry.Nb, assistant);
        var fixture = exam.FixtureOf(stages[0])!;
        var stopwatch = Stopwatch.StartNew();

        // nb's programs are compiled on the host as on the bare path, one per stage, with the checkout where the image has it.
        var manifests = new Dictionary<string, Manifest>();
        string? lost = null, lostAt = null, brokeAt = null, broke = null;
        var programs = Path.Combine(tryDir, Layout.StageProgramsDir);
        if (!assistant.IsVendor) { if (Directory.Exists(programs)) Directory.Delete(programs, recursive: true); Directory.CreateDirectory(programs); }
        foreach (var (stage, i) in stages.Select((s, i) => (s, i)))
        {
            var attemptDir = AttemptDir(assistant, stage, n);
            Directory.CreateDirectory(attemptDir);
            var program = assistant.IsVendor ? null : ResolveProgram(exam.ProgramTemplate, assistant, stage, n, HarborCompile.Workdir, Context(root, entry, exam, assistant, stage, n));
            var manifest = NewManifest(assistant, stage, n, fixture, Sha256(program ?? stage.Prompt!)) with { Runner = null, Nb = null, Transcript = new TranscriptRef(Layout.TrajectoryFile, "atif") };
            manifest.Agent = new AgentTrial(assistant.Agent!, null, assistant.Model ?? "", HarborCompile.AgentKwargs(assistant, route, assistant.IsVendor ? null : new NbAttempt(programs, entry.Nb.Config)), HarborCompile.AgentEnv(assistant, route), entry.Harbor!.Version, null);
            manifest.Tries++;
            manifest.Started = Now();
            SetStatus(attemptDir, manifest, AttemptStatus.Running);
            manifests[stage.Id!] = manifest;
            if (program is null || lost is not null) continue;
            File.WriteAllText(Path.Combine(attemptDir, Layout.ProgramFile), program);
            var (compiled, compileError) = Compile(attemptDir, program);
            if (compileError is not null) { lost = $"program compile failed: {compileError}"; lostAt = stage.Id; }
            else File.WriteAllText(Path.Combine(programs, $"{i + 1}.jsonl"), compiled!);
        }

        string? trialDir = null;
        var steps = new Dictionary<string, string>();
        if (lost is null)
        {
            // Harbor's agent timeout is each step's, so the trial may take the wall limit once per stage.
            (trialDir, lost) = HarborRun.Trial(config.Harbor?.Uv ?? new HarborConfig().Uv, entry.Harbor!.Version, entry.Harbor.Engine, tryDir, assistant, taskDir, route,
                exam.Def.Limits!.WallMs!.Value * stages.Count, HarborCompile.AgentsDir(entryDir), assistant.IsVendor ? null : new NbAttempt(programs, entry.Nb.Config));
            if (trialDir is not null) (steps, lost) = Ingest.Steps(trialDir);
            if (lost is not null) lostAt = stages.FirstOrDefault(s => !steps.ContainsKey(s.Id!))?.Id ?? stages[^1].Id;
        }

        var lostSeen = false;
        foreach (var stage in stages)
        {
            var attemptDir = AttemptDir(assistant, stage, n);
            var manifest = manifests[stage.Id!];
            void End(string status, string? reason)
            {
                manifest.Ended = Now();
                manifest.StatusReason = reason;
                SetStatus(attemptDir, manifest, status);
                var core = Path.Combine(attemptDir, Layout.CoreFile);
                log.WriteLine($"  {assistant.Id}/{n}/{stage.Id}  {status}  {reason ?? (File.Exists(core) ? ReadJson<CountedCore>(core)?.ExitReason : null)}");
            }
            // Stages Harbor finished before the one that lost the try are still read, as the bare path leaves them: none of a lost try counts, but it is there to look at.
            if (lostAt is not null && lostAt != stage.Id && (lostSeen || trialDir is null)) { End(AttemptStatus.Skipped, $"not {(trialDir is null ? "launched" : "read")}: the try was lost at stage {lostAt}"); continue; }
            if (lostAt == stage.Id) { lostSeen = true; End(AttemptStatus.Failed, lost); continue; }
            if (brokeAt is not null) { End(AttemptStatus.Skipped, $"not reached: stage {brokeAt} was stopped ({broke}), and this exam's stages are chained"); continue; }
            if (!steps.TryGetValue(stage.Id!, out var stepDir)) { lost = "Harbor did not run this step"; lostAt = stage.Id; lostSeen = true; End(AttemptStatus.Failed, lost); continue; }
            var failure = Ingest.Trial(stepDir, attemptDir, assistant, manifest, assistant.IsVendor ? assistant.AgentVersion! : entry.Versions["nb"]);
            var exit = failure is null ? ReadJson<CountedCore>(Path.Combine(attemptDir, Layout.CoreFile))?.ExitReason : null;
            if (exit is not null && StageEnd.DidNotRun(exit)) failure = $"did not run: {exit}";
            if (failure is not null) { lost = failure; lostAt = stage.Id; lostSeen = true; End(AttemptStatus.Failed, failure); continue; }
            manifest.DurationMs = ReadJson<CountedCore>(Path.Combine(attemptDir, Layout.CoreFile))?.DurationMs;
            End(AttemptStatus.Completed, null);
            if (exam.Chained && !StageEnd.DidEnd(exit!)) { brokeAt = stage.Id; broke = exit; }
        }
        WriteStatusCounts();
        log.WriteLine($"  {assistant.Id}/{n}  {(lostAt is not null ? $"lost at {lostAt}: {lost}" : brokeAt is not null ? $"broke at {brokeAt} ({broke})" : "whole")}  {stopwatch.Elapsed.TotalSeconds:F1}s");
    }

    /// <summary>One stage of a try: nb on the stage's program, in the try's workspace. Why the try is lost, or else the stage's exit reason.</summary>
    (string? Failure, string? ExitReason) RunStage(Assistant assistant, StageDef stage, int n, bool collectDiff)
    {
        var attempt = Context(root, entry, exam, assistant, stage, n);
        var attemptDir = attempt.AttemptDir;
        Directory.CreateDirectory(attemptDir);
        var program = OraclePrefix(exam, stage) + ResolveProgram(exam.ProgramTemplate, assistant, stage, n, attempt.WorkMount, attempt);
        var manifest = NewManifest(assistant, stage, n, attempt.Fixture, Sha256(program));
        manifest.Tries++;
        manifest.Started = Now();
        SetStatus(attemptDir, manifest, AttemptStatus.Running);
        File.WriteAllText(Path.Combine(attemptDir, Layout.ProgramFile), program);

        var stopwatch = Stopwatch.StartNew();
        var (compiled, compileError) = Compile(attemptDir, program);
        var failure = compileError is not null ? $"program compile failed: {compileError}" : RunNb(attemptDir, attempt.WorkDir, compiled!, attempt.Environment(), manifest);
        var exit = failure is null ? ExitReasonOf(attemptDir) : null;
        if (exit is not null && StageEnd.DidNotRun(exit)) failure = $"did not run: {exit}";
        // The workspace as this stage left it, before the next stage restores or builds on it.
        if (failure is null && collectDiff && Checkout.CollectDiff(attempt.WorkDir, attemptDir) is { } diffError) failure = $"diff collection failed: {diffError}";
        if (failure is null && collectDiff) manifest.WorkspaceHash = Tree.Hash(attempt.WorkDir, new HashSet<string> { ".git" });
        manifest.StartTree = startTree;
        stopwatch.Stop();

        manifest.Ended = Now();
        manifest.DurationMs = stopwatch.ElapsedMilliseconds;
        manifest.StatusReason = failure;
        SetStatus(attemptDir, manifest, failure is null ? AttemptStatus.Completed : AttemptStatus.Failed);
        log.WriteLine($"  {assistant.Id}/{n}/{stage.Id}  {manifest.Status}  {failure ?? exit}  {stopwatch.Elapsed.TotalSeconds:F1}s");
        return (failure, exit);
    }

    Manifest NewManifest(Assistant assistant, StageDef task, int n, Fixture? fixture, string programHash) => new()
    {
        Id = Layout.NewAttemptId(), Exam = exam.Id, Log = entry.Id, Configuration = RefOf(assistant), Stage = RefOf(task), Attempt = n,
        Place = entry.Stages!.IndexOf(task.Id!) + 1,
        Runner = assistant.Runner, Harness = assistant.Harness, Provider = assistant.Provider, Model = assistant.Model,
        Fixture = fixture is null ? null : new FixtureRef(fixture.Id, fixture.Hash),
        Nb = entry.Nb,
        Host = entry.Host, Versions = entry.Versions, ProgramHash = programHash,
        Try = entry.IdOf(assistant.Id!, n), Values = exam.Axes.Seen(exam.Axes.Of(assistant.Id!)!),
    };

    /// <summary>A stage that was not launched, or one whose try failed around it: its manifest and status, with why.</summary>
    void Mark(Assistant assistant, StageDef stage, int n, string status, string reason)
    {
        var attemptDir = AttemptDir(assistant, stage, n);
        Directory.CreateDirectory(attemptDir);
        var manifestFile = Path.Combine(attemptDir, Layout.ManifestFile);
        var manifest = File.Exists(manifestFile) ? ReadManifest(manifestFile) : NewManifest(assistant, stage, n, exam.FixtureOf(stage), "");
        manifest.StatusReason = reason;
        SetStatus(attemptDir, manifest, status);
        log.WriteLine($"  {assistant.Id}/{n}/{stage.Id}  {status}  {reason}");
    }

    /// <summary>
    /// One Harbor trial of a plain task: a try that is one stage with an answer sheet (<see cref="HarborCompile.Plain"/>),
    /// which the simulated user can only answer as a task. It is kept where any try is, with the manifest a stage has,
    /// and a stage that did not run loses it.
    /// </summary>
    void RunAgentTask(Assistant assistant, StageDef task, int n, string attemptDir, string taskDir)
    {
        Directory.CreateDirectory(attemptDir);
        var route = Route(root, config, entry.Nb, assistant);
        var fixture = exam.FixtureOf(task)!;
        // nb's program is compiled on the host as on the bare path, with the checkout where the image has it.
        var program = assistant.IsVendor ? null : ResolveProgram(exam.ProgramTemplate, assistant, task, n, HarborCompile.Workdir, Context(root, entry, exam, assistant, task, n));
        var manifest = NewManifest(assistant, task, n, fixture, Sha256(program ?? File.ReadAllText(Path.Combine(taskDir, "instruction.md")))) with { Runner = null, Nb = null, Transcript = new TranscriptRef(Layout.TrajectoryFile, "atif") };
        var nb = program is null ? null : new NbAttempt(Path.Combine(attemptDir, Layout.CompiledProgramFile), entry.Nb.Config);
        var oracle = OracleAttemptOf(assistant, task);
        manifest.Agent = new AgentTrial(assistant.Agent!, null, assistant.Model ?? "", HarborCompile.AgentKwargs(assistant, route, nb), HarborCompile.AgentEnv(assistant, route), entry.Harbor!.Version, null);
        manifest.Oracle = null;
        manifest.Tries++;
        manifest.Started = Now();
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
            (trialDir, failure) = HarborRun.Trial(config.Harbor?.Uv ?? new HarborConfig().Uv, entry.Harbor.Version, entry.Harbor.Engine, attemptDir, assistant, taskDir, route, exam.Def.Limits!.WallMs!.Value, HarborCompile.AgentsDir(entryDir), nb, oracle);
        if (trialDir is not null) failure = Ingest.Trial(trialDir, attemptDir, assistant, manifest, assistant.IsVendor ? assistant.AgentVersion! : entry.Versions["nb"], bridged: oracle is not null);
        stopwatch.Stop();
        var exitReason = failure is null ? ReadJson<CountedCore>(Path.Combine(attemptDir, Layout.CoreFile))?.ExitReason : null;
        // A stage that did not run loses its try, here as in a try of steps.
        if (exitReason is not null && StageEnd.DidNotRun(exitReason)) failure = $"did not run: {exitReason}";

        manifest.Ended = Now();
        manifest.DurationMs = stopwatch.ElapsedMilliseconds;
        manifest.StatusReason = failure;
        SetStatus(attemptDir, manifest, failure is null ? AttemptStatus.Completed : AttemptStatus.Failed);
        WriteStatusCounts();
        log.WriteLine($"  {assistant.Id}/{n}/{task.Id}  {manifest.Status}  {failure ?? exitReason}");
        log.WriteLine($"  {assistant.Id}/{n}  {(failure is null ? "whole" : $"lost at {task.Id}: {failure}")}  {stopwatch.Elapsed.TotalSeconds:F1}s");
    }

    /// <summary>
    /// The simulated user of a stage with an answer sheet: the host nb judges, with its config, as the provider that does
    /// the work unless the exam names a judge, which is what nb's in-process oracle does on the bare path.
    /// </summary>
    OracleAttempt? OracleAttemptOf(Assistant assistant, StageDef task) => exam.OracleOf(task) is { } sheet
        ? new OracleAttempt(sheet, entry.Nb.Path, entry.Nb.Config, exam.Def.Oracle?.Judge ?? assistant.Provider, exam.OracleTurns,
            Path.Combine(HarborCompile.AgentsDir(entryDir), HarborCompile.OracleTemplateFile))
        : null;

    /// <summary>
    /// Where an agent's model calls go, ${VAR} resolved now: the key never reaches a file. A vendor agent's is
    /// proctor.json's agents block; nb's is its own config's provider entry, whose endpoint variables go to the agent's
    /// process by value and whose key goes by name, as on the bare path, where nb resolves them itself. A provider
    /// with no endpoint (Mock) reaches nothing.
    /// </summary>
    static AgentRoute Route(string root, AuspexConfig config, ResolvedNb nb, Assistant assistant)
    {
        if (!assistant.IsVendor) return NbRoute(nb, assistant);
        var endpoint = config.Agents?.GetValueOrDefault(assistant.Agent!)
            ?? throw new AuspexException($"{config.Source} has no agents.{assistant.Agent} ({assistant.Id}): add {{\"base_url\", \"api_key\"}} for it, as in proctor.example.json");
        var baseUrl = ResolveEnv(endpoint.BaseUrl!, $"{config.Source} agents.{assistant.Agent}.base_url").TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _)) throw new AuspexException($"{config.Source} agents.{assistant.Agent}.base_url: '{baseUrl}' is not a URL");
        var agentConfig = endpoint.Config is null ? null : Path.GetFullPath(Path.Combine(Layout.Exams(root), endpoint.Config));
        if (agentConfig is not null && !File.Exists(agentConfig)) throw new AuspexException($"{config.Source} agents.{assistant.Agent}.config: {agentConfig} not found");
        return new AgentRoute(baseUrl, endpoint.ApiKey is null ? null : ResolveEnv(endpoint.ApiKey, $"{config.Source} agents.{assistant.Agent}.api_key"), HarborCompile.KeyEnv(assistant.Agent!), Config: agentConfig);
    }

    /// <summary>
    /// What a run on a local engine relays to its agents (Run/Relay.cs), or null when nothing: the ports of this machine
    /// its tasks reach, each of which must answer now, since a model that is not running is better said here than found
    /// by every attempt timing out.
    /// </summary>
    static ResolvedRelay? Relay(string entryId, Exam exam, IEnumerable<(Assistant Assistant, StageDef Stage)> pairs, Func<Assistant, AgentRoute> route)
    {
        var wanted = pairs.Select(p => (Fixture: exam.FixtureOf(p.Stage)!, p.Assistant, Route: route(p.Assistant))).ToList();
        var ports = wanted.SelectMany(w => HarborCompile.RelayPorts(w.Fixture, w.Route)).Distinct().Order().ToList();
        if (ports.Count == 0) return null;
        var problems = new List<string>();
        foreach (var port in ports.Where(p => !HostRelay.Answers(p)))
        {
            var models = wanted.Where(w => w.Route.BaseUrl is { } url && new Uri(url).Port == port && w.Fixture.Def.Image?.HostPorts?.Contains(port) != true).ToList();
            problems.Add(models.Count > 0
                ? $"'{models[0].Assistant.Id}': its model is at {models[0].Route.BaseUrl}, which is this machine's port {port}, and nothing answers there; start it, or if a service of fixture '{models[0].Fixture.Id}' answers on that port beside the agent, list it in the fixture's image.serves"
                : $"fixture '{wanted.First(w => w.Fixture.Def.Image?.HostPorts?.Contains(port) == true).Fixture.Id}' names this machine's port {port} (image.host_ports), and nothing answers there; start what it is for");
        }
        if (problems.Count > 0) throw new AuspexException(string.Join("\n", problems));
        return new ResolvedRelay(HostRelay.Dir(entryId), ports);
    }

    static readonly System.Text.RegularExpressions.Regex EnvRef = new(@"\$\{([A-Za-z_][A-Za-z0-9_]*)\}");

    static AgentRoute NbRoute(ResolvedNb nb, Assistant assistant)
    {
        var where = nb.Config ?? "nb's config";
        var providers = nb.Config is null ? null : JsonNode.Parse(File.ReadAllText(nb.Config), documentOptions: new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })?["ChatProviders"] as JsonArray;
        var entry = providers?.OfType<JsonObject>().FirstOrDefault(p => p["Name"]?.GetValue<string>() == assistant.Provider)
            ?? throw new AuspexException($"'{assistant.Id}': provider '{assistant.Provider}' is not in {where} ChatProviders; nb through Harbor reads its endpoint from there");
        var endpoint = entry["Endpoint"]?.GetValue<string>();
        if (endpoint is null) return new AgentRoute(null, null);
        var env = EnvRef.Matches(endpoint).Select(m => m.Groups[1].Value).Distinct().ToDictionary(v => v, v => ResolveEnv($"${{{v}}}", $"{where} {assistant.Provider}.Endpoint"));
        var baseUrl = EnvRef.Replace(endpoint, m => env[m.Groups[1].Value]);
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _)) throw new AuspexException($"{where} {assistant.Provider}.Endpoint: '{baseUrl}' is not a URL");
        var key = entry["ApiKey"]?.GetValue<string>() is { } k && EnvRef.Match(k) is { Success: true } m ? m.Groups[1].Value : null;
        return new AgentRoute(baseUrl, key is null ? null : ResolveEnv($"${{{key}}}", $"{where} {assistant.Provider}.ApiKey"), key, env);
    }

    /// <summary>nb's layer from proctor.json harbor.nb, which must be the same nb as the host binary that compiles the programs and runs nb bare.</summary>
    static string NbImage(string root, AuspexConfig config, string hostVersion)
    {
        var publish = config.Harbor?.Nb ?? throw new AuspexException($"nb runs through Harbor on its self-contained linux-x64 publish: set harbor.nb in {config.Source} to its directory");
        var (tag, version) = HarborRun.NbImage(NbPublish(root, config), config.HarborOrDefault);
        if (version != hostVersion)
            throw new AuspexException($"nb's publish ({config.Source} harbor.nb) is {version}, the host nb is {hostVersion}: publish the same nb, so the program compiled on the host is the program the image runs");
        return tag;
    }

    /// <summary>The directory of nb's publish, proctor.json harbor.nb resolved against exams/.</summary>
    static string NbPublish(string root, AuspexConfig config) => Path.GetFullPath(Path.Combine(Layout.Exams(root), config.Harbor!.Nb!));

    static string ResolveEnv(string value, string field) =>
        System.Text.RegularExpressions.Regex.Replace(value, @"\$\{([A-Za-z_][A-Za-z0-9_]*)\}", m => Environment.GetEnvironmentVariable(m.Groups[1].Value)
            ?? throw new AuspexException($"{m.Groups[1].Value} is not set in the environment or {EnvFile.DefaultPath()} ({field})"));

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
    string? RunHook(string? script, IDictionary<string, string> env, string logFile) => RunHook(exam, script, env, logFile);

    internal static string? RunHook(Exam exam, string? script, IDictionary<string, string> env, string logFile)
    {
        if (script is null) return null;
        var result = Subprocess.Run(Path.GetFullPath(Path.Combine(exam.Dir, script)), [], exam.Dir, env);
        File.WriteAllText(logFile, result.Stdout + (result.Stderr.Length > 0 ? "\n--- stderr ---\n" + result.Stderr : ""));
        if (!result.Started) return result.Stderr;
        return result.ExitCode == 0 ? null : $"{script} exited {result.ExitCode}: {result.FirstStderrLine}";
    }

    Ref RefOf(Assistant assistant) => new(assistant.Id!, assistant.Hash);
    Ref RefOf(StageDef task) => new(task.Id!, exam.StageHashes[task.Id!]);

    static void SetStatus(string attemptDir, Manifest manifest, string status)
    {
        manifest.Status = status;
        File.WriteAllText(Path.Combine(attemptDir, Layout.StatusFile), status + "\n");
        WriteJson(Path.Combine(attemptDir, Layout.ManifestFile), manifest);
    }

    void WriteStatusCounts()
    {
        var counts = AttemptStatus.All.ToDictionary(s => s, _ => 0);
        foreach (var a in entry.Attempts) counts[ReadStatus(Layout.Attempt(root, exam.Id, a.Stage, entry.Stages!, a.Id!))]++;
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
    /// needs the file. Empty for a stage without a sheet.
    /// </summary>
    public static string OraclePrefix(Exam exam, StageDef task)
    {
        if (exam.OracleOf(task) is not { } sheet) return "";
        var prefix = $"oracle @{sheet}\n";
        if (exam.Def.Oracle?.Judge is { } judge) prefix += $"oracle provider {judge}\n";
        return prefix + $"budget oracle_turns {exam.OracleTurns}\n";
    }

    /// <summary>Fill the template. {{work}} is the path the model will see, which a runner may mount elsewhere. A prompt's newlines become nb continuation lines so the program stays one directive.</summary>
    public static string ResolveProgram(string template, Assistant assistant, StageDef c, int n, string workDir, AttemptContext? attempt = null)
    {
        var values = new Dictionary<string, string>
        {
            ["prompt"] = c.Prompt!.Replace("\r\n", "\n").Replace("\n", " \\\n"),
            ["stage"] = c.Id!,
            ["work"] = workDir,
            ["provider"] = assistant.Provider ?? "",
            ["model"] = assistant.Model ?? "",
            ["harness"] = assistant.Harness ?? "",
            ["configuration"] = assistant.Id!,
            ["attempt"] = n.ToString(),
        };
        return System.Text.RegularExpressions.Regex.Replace(template, @"\{\{\s*([^}]*?)\s*\}\}", m => m.Groups[1].Value switch
        {
            var name when name.StartsWith("axis.") => attempt?.Axis.GetValueOrDefault(name[5..]) ?? "",
            var name when name.StartsWith("param.") => attempt?.Params.GetValueOrDefault(name[6..]) ?? "",
            var name => values[name],
        });
    }

    internal static ResolvedNb ResolveNb(string root, Exam exam, AuspexConfig config, string? nbOverride, string? runnerOverride)
    {
        var nb = config.NbOrDefault;
        var examsDir = Layout.Exams(root);
        if (!File.Exists(Path.Combine(root, config.Source)) && File.Exists(Path.Combine(examsDir, "proctor.example.json")))
            throw new AuspexException("no exams/proctor.json: run `proctor setup` at a terminal, which writes it (skip every job for a config the playground runs on); or copy exams/proctor.example.json and nb.example.json beside themselves and point them at your gateway");
        var path = nbOverride ?? nb.Path;
        if (path.Contains(Path.DirectorySeparatorChar)) path = Path.GetFullPath(Path.Combine(nbOverride is null ? examsDir : Directory.GetCurrentDirectory(), path));
        else if (FindOnPath(path) is { } found) path = found;
        else throw new AuspexException($"nb not found: '{path}' is not on PATH; set nb.path in {config.Source} or pass --nb");
        if (!File.Exists(path)) throw new AuspexException($"nb not found at {path}");
        var configPath = nb.Config is null ? null : Path.GetFullPath(Path.Combine(examsDir, nb.Config));
        if (configPath is not null && !File.Exists(configPath)) throw new AuspexException($"nb config not found at {configPath} ({config.Source} nb.config){(File.Exists(Path.ChangeExtension(configPath, ".example.json")) ? $"; copy {Path.GetFileName(Path.ChangeExtension(configPath, ".example.json"))} beside it" : "")}");
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
        if (!File.Exists(path)) throw new AuspexException($"runner not found at {path} ({(runnerOverride is null ? $"exams/{exam.Id}/exam.json nb.runner" : "--runner")})");
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
    public static string AuspexVersion =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0";

    static Dictionary<string, string> Versions(string nbPath)
    {
        var proctor = AuspexVersion;
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
