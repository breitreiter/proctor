using System.Text.Json;
using System.Text.Json.Nodes;

namespace Proctor;

/// <summary>Runs the declared checks over the exam's current, completed attempts and writes checks.json beside the evidence.</summary>
static class Grade
{
    public record AttemptGrade(string Variant, string Task, int Attempt, string Status, Dictionary<string, Verdict>? Checks, bool? Pass, string? Invalid);

    /// <summary>Grade the given attempts, each a current one from the bank; one that did not complete is returned ungraded. With a `--judge a=b` client, compare instead.</summary>
    public static List<AttemptGrade> Attempts(string root, Exam exam, List<BankAttempt> attempts, TextWriter log, JudgeClient? judges = null)
    {
        if (judges is { Comparing: true }) return Compare(root, exam, attempts, log, judges);
        var grades = new List<AttemptGrade>();
        if (judges is not null)
            foreach (var note in Judge.SameFamilyNotes(exam, judges)) log.WriteLine($"  note: {note}");
        foreach (var a in attempts)
        {
            var (variant, c, n) = (exam.Variants.First(v => v.Id == a.Variant), exam.Tasks.First(t => t.Id == a.Task), a.Attempt);
            var attemptDir = a.Dir(root);
            var status = Runner.ReadStatus(attemptDir);
            if (status != AttemptStatus.Completed)
            {
                grades.Add(new AttemptGrade(variant.Id!, c.Id!, n, status, null, null, null));
                continue;
            }
            var attempt = Runner.Context(root, a.Entry, exam, variant, c, n) with { Judges = judges };
            // A stage's script checks run on the workspace as that stage left it, which the try's own checkout no longer is once a later stage has run: the fixture with the stage's diff, rebuilt beside it.
            if (a.Staged && !variant.IsAgent) attempt = attempt with { WorkDir = Layout.WorkAsStageLeftIt(root, exam.Id, variant.Id!, n, Path.GetFileName(attemptDir), a.Entry.IdOf(a.Variant, n)) };
            // A script check may need the checkout; after it is gone, the fixture plus the diff is the same tree. An agent attempt's scripts ran in the verifier.
            if (!variant.IsAgent && attempt.Fixture is not null && exam.ChecksFor(c).Values.Any(k => k.Spec.ContainsKey("script")) && Checkout.Restore(attempt.Fixture, attempt.WorkDir, attemptDir, attempt.Overlays, a.Staged ? Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(attemptDir))!, Layout.SetupPatchFile) : null) is { } error)
                log.WriteLine($"  {a.Label}  could not restore the checkout: {error}");
            var checks = Attempt(attempt, exam, Transcript.Read(attemptDir, Before(root, exam, a)));
            var pass = Pass(checks, exam.PassFor(c));
            var invalid = Invalid(checks, exam.ValidityFor(c));
            grades.Add(new AttemptGrade(variant.Id!, c.Id!, n, status, checks, pass, invalid));
            log.WriteLine($"  {a.Label}  {(invalid is not null ? "invalid" : pass switch { true => "pass", false => "fail", null => Outdated(checks, exam.PassFor(c)) is null ? "undecided" : "outdated" })}  {string.Join("  ", checks.Where(k => k.Value.Result != Verdict.Pass).Select(k => $"{k.Key}={k.Value.Result}"))}");
        }
        return grades;
    }

    /// <summary>
    /// What grading these attempts would ask of each judge, found without asking: per judge, the verdicts to ask, the votes
    /// they come to, and the verdicts already on file, which cost nothing. A check that does not apply asks no judge. One line
    /// per judge, none when no check asks a model.
    /// </summary>
    public static List<string> Cost(string root, Exam exam, List<BankAttempt> attempts, JudgeClient judges)
    {
        judges.Counted.Clear();
        judges.Counting = true;
        try
        {
            foreach (var a in attempts)
            {
                var (variant, c) = (exam.Variants.First(v => v.Id == a.Variant), exam.Tasks.First(t => t.Id == a.Task));
                var attemptDir = a.Dir(root);
                var asking = exam.ChecksFor(c).Where(k => Standard.Applies(exam, k.Value, a.Variant) && Checks.ModelChecks(k.Value.Spec).Any()).ToList();
                if (asking.Count == 0 || Runner.ReadStatus(attemptDir) != AttemptStatus.Completed) continue;
                var attempt = Runner.Context(root, a.Entry, exam, variant, c, a.Attempt) with { Judges = judges };
                var transcript = Transcript.Read(attemptDir, Before(root, exam, a));
                foreach (var (name, check) in asking)
                    foreach (var (kind, spec) in Checks.ModelChecks(check.Spec)) Judge.Evaluate(kind, spec, name, attempt, transcript);
            }
        }
        finally { judges.Counting = false; judges.Errors.Clear(); }
        return judges.Counted.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k =>
            $"  {k.Key}: {(k.Value.ToAsk == 0 ? "nothing to ask" : $"{k.Value.ToAsk} verdict{(k.Value.ToAsk == 1 ? "" : "s")} to ask, {k.Value.Votes} vote{(k.Value.Votes == 1 ? "" : "s")} in all")}; {k.Value.OnFile} already on file, which cost nothing").ToList();
    }

    /// <summary>On a chained exam, the stage before this one in the same try: its diff is in this one's, and is taken out to see what this stage changed. Null otherwise.</summary>
    static string? Before(string root, Exam exam, BankAttempt a) =>
        a.Staged && exam.Chained && a.Entry.Stages!.IndexOf(a.Task) is > 0 and var i ? Layout.Attempt(root, exam.Id, a.Variant, a.Entry.Stages[i - 1], a.Attempt, a.Entry.Stages, a.Entry.IdOf(a.Variant, a.Attempt)) : null;

    /// <summary>
    /// A `--judge a=b` pass: only the checks whose declared judge is remapped are evaluated, by b, into b's verdict files
    /// beside a's; checks.json is read, compared against and never written. The grades returned are the ones on file.
    /// </summary>
    static List<AttemptGrade> Compare(string root, Exam exam, List<BankAttempt> attempts, TextWriter log, JudgeClient judges)
    {
        var grades = new List<AttemptGrade>();
        int compared = 0, agreed = 0;
        foreach (var a in attempts)
        {
            var (variant, c, n) = (exam.Variants.First(v => v.Id == a.Variant), exam.Tasks.First(t => t.Id == a.Task), a.Attempt);
            var attemptDir = a.Dir(root);
            var status = Runner.ReadStatus(attemptDir);
            var existing = status == AttemptStatus.Completed ? ReadChecks(attemptDir) : null;
            if (existing is null)
            {
                grades.Add(new AttemptGrade(variant.Id!, c.Id!, n, status, null, null, null));
                if (status == AttemptStatus.Completed) log.WriteLine($"  {a.Label}  no checks.json; run grade without --judge first");
                continue;
            }
            var attempt = Runner.Context(root, a.Entry, exam, variant, c, n) with { Judges = judges };
            var transcript = Transcript.Read(attemptDir, Before(root, exam, a));
            foreach (var (name, check) in exam.ChecksFor(c))
            {
                if (!Checks.ModelChecks(check.Spec).Any(m => judges.Remaps(m.Name, m.Spec, name))) continue;
                var (from, to) = Checks.ModelChecks(check.Spec).Select(m => (judges.Declared(Judge.KindOf(m.Name), (m.Spec["with"] as JsonValue)?.GetValue<string>(), name).Name, judges.Resolve(Judge.KindOf(m.Name), (m.Spec["with"] as JsonValue)?.GetValue<string>(), name).Name)).First();
                var other = Checks.Evaluate(check, attempt, transcript, name);
                var applied = existing.GetValueOrDefault(name);
                compared++;
                if (applied?.Result == other.Result) agreed++;
                log.WriteLine($"  {a.Label}  {name}: {from}={applied?.Result ?? "none"} ({applied?.Reason})  {to}={other.Result} ({other.Reason})");
            }
            grades.Add(new AttemptGrade(variant.Id!, c.Id!, n, status, existing, Pass(existing, exam.PassFor(c)), Invalid(existing, exam.ValidityFor(c))));
        }
        log.WriteLine($"  {agreed} of {compared} verdicts agree; checks.json unchanged");
        return grades;
    }

    /// <summary>
    /// Grade one attempt and write its checks.json. Returns the verdict map. An agent attempt's script checks ran in the
    /// verifier when it was made; one whose script, the files beside it or its expected values have changed since, or that
    /// did not exist then, is outdated, never the old verdict read back as if current.
    /// </summary>
    public static Dictionary<string, Verdict> Attempt(AttemptContext attempt, Exam exam, Transcript transcript)
    {
        var outdated = attempt.ScriptsInVerifier ? HarborCompile.Outdated(attempt.VerifierTask, exam, attempt.Task, attempt.Variant) : [];
        // A check that does not apply in this configuration is not asked: no script runs and no judge is called for it.
        var verdicts = exam.ChecksFor(attempt.Task).Where(k => !(attempt.Vendor && Checks.NbOnly(k.Value.Spec))).ToDictionary(k => k.Key,
            k => Explained(!Standard.Applies(exam, k.Value, attempt.Variant) ? new Verdict(Verdict.NotApplicable, $"applies only when {k.Value.Spec[Checks.When]}")
                : outdated.TryGetValue(k.Key, out var why) ? new Verdict(Verdict.Outdated, why) : Checks.Evaluate(k.Value, attempt, transcript, k.Key), k.Value, exam));
        Runner.WriteJson(Path.Combine(attempt.AttemptDir, Layout.ChecksFile), verdicts);
        Standard.Keep(attempt.AttemptDir, exam, attempt.Task, verdicts);
        return verdicts;
    }

    /// <summary>
    /// A verdict with the check as it was when it graded (check-evidence.md, C2): what it does, why it exists, and a script
    /// check's script relative to proctor's root. A viewer shows these as written and derives none of them.
    /// </summary>
    static Verdict Explained(Verdict verdict, CheckDef check, Exam exam)
    {
        var root = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(exam.Dir)))!;
        var script = check.Spec["script"] is JsonValue v && v.TryGetValue<string>(out var path) ? Path.GetRelativePath(root, Path.GetFullPath(Path.Combine(check.Dir, path))) : null;
        return verdict with { Describes = Checks.Describe(check.Spec), Why = Checks.WhyOf(check.Spec), Script = script };
    }

    public static Dictionary<string, Verdict>? ReadChecks(string attemptDir)
    {
        var file = Path.Combine(attemptDir, Layout.ChecksFile);
        return File.Exists(file) ? JsonSerializer.Deserialize<Dictionary<string, Verdict>>(File.ReadAllText(file), Exam.JsonOptions) : null;
    }

    /// <summary>
    /// The headline pass: true when every check named in grading.pass passed; false when any failed, errored or is missing;
    /// null (undecided) when none failed but one could not decide or is outdated. An undecided attempt is out of the pass rate
    /// on both sides: a check that cannot decide is the check's weakness, not the variant's, and an outdated one is the
    /// attempt's age, not the variant's.
    /// </summary>
    public static bool? Pass(Dictionary<string, Verdict> checks, List<string> passChecks)
    {
        // A headline check that does not apply here is no part of this attempt's headline.
        var results = passChecks.Select(name => checks.TryGetValue(name, out var v) ? v.Result : Verdict.Error).Where(r => r != Verdict.NotApplicable).ToList();
        if (results.Any(r => r is Verdict.Fail or Verdict.Error)) return false;
        return results.Any(r => r is Verdict.NeedsJudge or Verdict.Outdated) ? null : true;
    }

    /// <summary>Why the attempt is undecided, or null: the first pass check that is outdated, else the first that needs a judge, as "check: reason", when nothing failed.</summary>
    public static string? Undecided(Dictionary<string, Verdict> checks, List<string> passChecks) =>
        Pass(checks, passChecks) is null ? Outdated(checks, passChecks) ?? First(checks, passChecks, Verdict.NeedsJudge) : null;

    /// <summary>Why the attempt is undecided because a pass check is outdated, or null: the first such check, as "check: reason", when nothing failed.</summary>
    public static string? Outdated(Dictionary<string, Verdict> checks, List<string> passChecks) =>
        Pass(checks, passChecks) is null ? First(checks, passChecks, Verdict.Outdated) : null;

    /// <summary>What grade says when any attempt holds an outdated verdict: how many attempts, which checks, and how to make them again; null when none does.</summary>
    public static string? OutdatedSummary(string examId, List<AttemptGrade> grades)
    {
        var holding = grades.Where(g => g.Checks?.Values.Any(v => v.Result == Verdict.Outdated) == true).ToList();
        if (holding.Count == 0) return null;
        var checks = holding.SelectMany(g => g.Checks!.Where(k => k.Value.Result == Verdict.Outdated).Select(k => k.Key)).Distinct().Order(StringComparer.Ordinal).ToList();
        return $"{holding.Count} attempt{(holding.Count == 1 ? "" : "s")} {(holding.Count == 1 ? "holds" : "hold")} outdated verdicts for {string.Join(", ", checks)}: "
            + $"on an agent variant a script check runs when the attempt is made, and {(checks.Count == 1 ? "this check has" : "these checks have")} changed or been added since, so those verdicts are left out of every rate. "
            + $"Make the attempts again: move {(holding.Count == 1 ? "its directory" : "their directories")} out of {Layout.BankDir}/{examId}/ ({string.Join(" ", holding.Select(g => $"{g.Variant}/{g.Task}/{g.Attempt}"))}) and run `proctor run {examId}`, which makes what is missing with the checks as they are now.";
    }

    /// <summary>
    /// For each check, how many of the graded attempts it has a pass or fail for, of those it applies to, as lines; then
    /// how many of those it had none for before this grade, and how many it could not decide and why.
    /// </summary>
    /// <summary>
    /// The verdicts each attempt held before this grade, keyed as <see cref="ByCheck"/> looks them up: variant, task,
    /// attempt. Not an attempt's label, which for a stage of a try puts the try before the stage: keyed by label, no
    /// stage was ever found, and every verdict of a staged exam was counted as new on every grade (issue #13).
    /// </summary>
    public static Dictionary<string, Dictionary<string, Verdict>?> Before(string root, IEnumerable<BankAttempt> attempts) =>
        attempts.ToDictionary(a => $"{a.Variant}/{a.Task}/{a.Attempt}", a => ReadChecks(a.Dir(root)));

    public static IEnumerable<string> ByCheck(List<AttemptGrade> grades, Dictionary<string, Dictionary<string, Verdict>?> before)
    {
        static bool Decided(Verdict? v) => v?.Result is Verdict.Pass or Verdict.Fail;
        var graded = grades.Where(g => g.Checks is not null).ToList();
        var names = graded.SelectMany(g => g.Checks!.Keys).Distinct().ToList();
        if (names.Count == 0) yield break;
        var width = names.Max(n => n.Length);
        foreach (var name in names)
        {
            var on = graded.Where(g => g.Checks!.ContainsKey(name)).ToList();
            var decided = on.Where(g => Decided(g.Checks![name])).ToList();
            var fresh = decided.Count(g => !Decided(before.GetValueOrDefault($"{g.Variant}/{g.Task}/{g.Attempt}")?.GetValueOrDefault(name)));
            var not = on.Except(decided).GroupBy(g => g.Checks![name].Result).Select(r => $"{r.Count()} {r.Key switch
            {
                Verdict.Outdated => "cannot be regraded from what was kept",
                Verdict.NeedsJudge => r.Count() == 1 ? "needs a judge" : "need a judge",
                Verdict.Error => "errored",
                Verdict.NotApplicable => r.Count() == 1 ? "does not apply" : "do not apply",
                var other => other,
            }}");
            yield return $"  {name.PadRight(width)}  graded {decided.Count} of {on.Count}{(fresh > 0 ? $"   {fresh} new" : "")}{string.Concat(not.Select(n => $"   {n}"))}";
        }
    }

    static string? First(Dictionary<string, Verdict> checks, List<string> passChecks, string result) =>
        passChecks.Where(name => checks.TryGetValue(name, out var v) && v.Result == result).Select(name => $"{name}: {checks[name].Reason}").FirstOrDefault();

    /// <summary>Why the attempt does not count, or null: the first validity check that failed, as "check: reason". An error is not a fail.</summary>
    public static string? Invalid(Dictionary<string, Verdict> checks, List<string>? validityChecks) =>
        (validityChecks ?? []).Where(name => checks.TryGetValue(name, out var v) && v.Result == Verdict.Fail)
            .Select(name => $"{name}: {checks[name].Reason}").FirstOrDefault();
}
