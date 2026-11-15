using System.Text.Json;
using System.Text.Json.Nodes;

namespace Proctor;

/// <summary>Runs the declared checks over the exam's current, completed attempts and writes checks.json beside the evidence.</summary>
static class Grade
{
    public record AttemptGrade(string Variant, string Task, int Attempt, string Status, Dictionary<string, Verdict>? Checks, bool? Pass, string? Invalid, string Batch);

    /// <summary>Grade the given attempts, each a current one from the pool; one that did not complete is returned ungraded. With a `--judge a=b` client, compare instead.</summary>
    public static List<AttemptGrade> Attempts(string root, Exam exam, List<PoolAttempt> attempts, TextWriter log, JudgeClient? judges = null)
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
                grades.Add(new AttemptGrade(variant.Id!, c.Id!, n, status, null, null, null, a.Batch.Id));
                continue;
            }
            var attempt = Runner.Context(root, a.Batch, exam, variant, c, n) with { Judges = judges };
            // A script check may need the checkout; after it is gone, the fixture plus the diff is the same tree. An agent attempt's scripts ran in the verifier.
            if (!variant.IsAgent && attempt.Fixture is not null && exam.ChecksFor(c).Values.Any(k => k.Spec.ContainsKey("script")) && Checkout.Restore(attempt.Fixture, attempt.WorkDir, attemptDir) is { } error)
                log.WriteLine($"  {a.Label}  could not restore the checkout: {error}");
            var checks = Attempt(attempt, exam, Transcript.Read(attemptDir));
            var pass = Pass(checks, exam.Grading.Pass!);
            var invalid = Invalid(checks, exam.Grading.Validity);
            grades.Add(new AttemptGrade(variant.Id!, c.Id!, n, status, checks, pass, invalid, a.Batch.Id));
            log.WriteLine($"  {a.Label}  {(invalid is not null ? "invalid" : pass switch { true => "pass", false => "fail", null => Outdated(checks, exam.Grading.Pass!) is null ? "undecided" : "outdated" })}  {string.Join("  ", checks.Where(k => k.Value.Result != Verdict.Pass).Select(k => $"{k.Key}={k.Value.Result}"))}");
        }
        return grades;
    }

    /// <summary>
    /// A `--judge a=b` pass: only the checks whose declared judge is remapped are evaluated, by b, into b's verdict files
    /// beside a's; checks.json is read, compared against and never written. The grades returned are the ones on file.
    /// </summary>
    static List<AttemptGrade> Compare(string root, Exam exam, List<PoolAttempt> attempts, TextWriter log, JudgeClient judges)
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
                grades.Add(new AttemptGrade(variant.Id!, c.Id!, n, status, null, null, null, a.Batch.Id));
                if (status == AttemptStatus.Completed) log.WriteLine($"  {a.Label}  no checks.json; run grade without --judge first");
                continue;
            }
            var attempt = Runner.Context(root, a.Batch, exam, variant, c, n) with { Judges = judges };
            var transcript = Transcript.Read(attemptDir);
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
            grades.Add(new AttemptGrade(variant.Id!, c.Id!, n, status, existing, Pass(existing, exam.Grading.Pass!), Invalid(existing, exam.Grading.Validity), a.Batch.Id));
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
        var outdated = attempt.ScriptsInVerifier ? HarborCompile.Outdated(attempt.VerifierTask, exam, attempt.Task, attempt.Variant, attempt.Batch) : [];
        var verdicts = exam.ChecksFor(attempt.Task).Where(k => !(attempt.Vendor && Checks.NbOnly(k.Value.Spec))).ToDictionary(k => k.Key,
            k => outdated.TryGetValue(k.Key, out var why) ? new Verdict(Verdict.Outdated, why) : Checks.Evaluate(k.Value, attempt, transcript, k.Key));
        Runner.WriteJson(Path.Combine(attempt.AttemptDir, Layout.ChecksFile), verdicts);
        return verdicts;
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
        var results = passChecks.Select(name => checks.TryGetValue(name, out var v) ? v.Result : Verdict.Error).ToList();
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
        var batches = holding.Select(g => g.Batch).Distinct().Order(StringComparer.Ordinal).ToList();
        return $"{holding.Count} attempt{(holding.Count == 1 ? "" : "s")} {(holding.Count == 1 ? "holds" : "hold")} outdated verdicts for {string.Join(", ", checks)}: "
            + $"on an agent variant a script check runs when the attempt is made, and {(checks.Count == 1 ? "this check has" : "these checks have")} changed or been added since, so those verdicts are left out of every rate. "
            + $"Make the attempts again: move {(batches.Count == 1 ? "batch" : "batches")} {string.Join(", ", batches)} out of {Layout.AttemptsDir}/ and run `proctor run {examId}`, which makes what is missing in a new batch with the checks as they are now.";
    }

    static string? First(Dictionary<string, Verdict> checks, List<string> passChecks, string result) =>
        passChecks.Where(name => checks.TryGetValue(name, out var v) && v.Result == result).Select(name => $"{name}: {checks[name].Reason}").FirstOrDefault();

    /// <summary>Why the attempt does not count, or null: the first validity check that failed, as "check: reason". An error is not a fail.</summary>
    public static string? Invalid(Dictionary<string, Verdict> checks, List<string>? validityChecks) =>
        (validityChecks ?? []).Where(name => checks.TryGetValue(name, out var v) && v.Result == Verdict.Fail)
            .Select(name => $"{name}: {checks[name].Reason}").FirstOrDefault();
}
