using System.Text.Json;
using System.Text.Json.Nodes;

namespace Proctor;

/// <summary>Runs the declared checks over every completed attempt and writes checks.json beside the evidence.</summary>
static class Grade
{
    public record AttemptGrade(string Variant, string Task, int Attempt, string Status, Dictionary<string, Verdict>? Checks, bool? Pass, string? Invalid);

    /// <param name="variants">Only these variants' attempts, when given; the others are not read and their checks.json is left as it was.</param>
    /// <param name="tasks">Only these tasks' attempts, when given, likewise.</param>
    public static List<AttemptGrade> Batch(string root, Batch batch, Exam exam, TextWriter log, JudgeClient? judges = null,
        IReadOnlyCollection<string>? variants = null, IReadOnlyCollection<string>? tasks = null)
    {
        var attempts = Runner.Attempts(exam).Where(x => (variants is null || variants.Contains(x.Variant.Id!)) && (tasks is null || tasks.Contains(x.Task.Id!))).ToList();
        if (judges is { Comparing: true }) return Compare(root, batch, exam, attempts, log, judges);
        var batchDir = Layout.Batch(root, batch.Id);
        var grades = new List<AttemptGrade>();
        if (judges is not null)
            foreach (var note in Judge.SameFamilyNotes(exam, judges)) log.WriteLine($"  note: {note}");
        foreach (var (variant, c, n) in attempts)
        {
            var attemptDir = Layout.Attempt(batchDir, variant.Id!, c.Id!, n);
            var status = Runner.ReadStatus(attemptDir);
            if (status != AttemptStatus.Completed)
            {
                grades.Add(new AttemptGrade(variant.Id!, c.Id!, n, status, null, null, null));
                continue;
            }
            var attempt = Runner.Context(root, batch, exam, variant, c, n) with { Judges = judges };
            // A script check may need the checkout; after it is gone, the fixture plus the diff is the same tree. An agent attempt's scripts ran in the verifier.
            if (!variant.IsAgent && attempt.Fixture is not null && exam.ChecksFor(c).Values.Any(k => k.Spec.ContainsKey("script")) && Checkout.Restore(attempt.Fixture, attempt.WorkDir, attemptDir) is { } error)
                log.WriteLine($"  {variant.Id}/{c.Id}/{n}  could not restore the checkout: {error}");
            var checks = Attempt(attempt, exam, Transcript.Read(attemptDir));
            var pass = Pass(checks, exam.Grading.Pass!);
            var invalid = Invalid(checks, exam.Grading.Validity);
            grades.Add(new AttemptGrade(variant.Id!, c.Id!, n, status, checks, pass, invalid));
            log.WriteLine($"  {variant.Id}/{c.Id}/{n}  {(invalid is not null ? "invalid" : pass switch { true => "pass", false => "fail", null => "undecided" })}  {string.Join("  ", checks.Where(k => k.Value.Result != Verdict.Pass).Select(k => $"{k.Key}={k.Value.Result}"))}");
        }
        return grades;
    }

    /// <summary>
    /// A `--judge a=b` pass: only the checks whose declared judge is remapped are evaluated, by b, into b's verdict files
    /// beside a's; checks.json is read, compared against and never written. The grades returned are the ones on file.
    /// </summary>
    static List<AttemptGrade> Compare(string root, Batch batch, Exam exam, List<(Variant Variant, TaskDef Task, int Attempt)> attempts, TextWriter log, JudgeClient judges)
    {
        var batchDir = Layout.Batch(root, batch.Id);
        var grades = new List<AttemptGrade>();
        int compared = 0, agreed = 0;
        foreach (var (variant, c, n) in attempts)
        {
            var attemptDir = Layout.Attempt(batchDir, variant.Id!, c.Id!, n);
            var status = Runner.ReadStatus(attemptDir);
            var existing = status == AttemptStatus.Completed ? ReadChecks(attemptDir) : null;
            if (existing is null)
            {
                grades.Add(new AttemptGrade(variant.Id!, c.Id!, n, status, null, null, null));
                if (status == AttemptStatus.Completed) log.WriteLine($"  {variant.Id}/{c.Id}/{n}  no checks.json; run grade without --judge first");
                continue;
            }
            var attempt = Runner.Context(root, batch, exam, variant, c, n) with { Judges = judges };
            var transcript = Transcript.Read(attemptDir);
            foreach (var (name, check) in exam.ChecksFor(c))
            {
                if (!Checks.ModelChecks(check.Spec).Any(m => judges.Remaps(m.Name, m.Spec, name))) continue;
                var (from, to) = Checks.ModelChecks(check.Spec).Select(m => (judges.Declared(Judge.KindOf(m.Name), (m.Spec["with"] as JsonValue)?.GetValue<string>(), name).Name, judges.Resolve(Judge.KindOf(m.Name), (m.Spec["with"] as JsonValue)?.GetValue<string>(), name).Name)).First();
                var other = Checks.Evaluate(check, attempt, transcript, name);
                var applied = existing.GetValueOrDefault(name);
                compared++;
                if (applied?.Result == other.Result) agreed++;
                log.WriteLine($"  {variant.Id}/{c.Id}/{n}  {name}: {from}={applied?.Result ?? "none"} ({applied?.Reason})  {to}={other.Result} ({other.Reason})");
            }
            grades.Add(new AttemptGrade(variant.Id!, c.Id!, n, status, existing, Pass(existing, exam.Grading.Pass!), Invalid(existing, exam.Grading.Validity)));
        }
        log.WriteLine($"  {agreed} of {compared} verdicts agree; checks.json unchanged");
        return grades;
    }

    /// <summary>Grade one attempt and write its checks.json. Returns the verdict map.</summary>
    public static Dictionary<string, Verdict> Attempt(AttemptContext attempt, Exam exam, Transcript transcript)
    {
        var verdicts = exam.ChecksFor(attempt.Task).Where(k => !(attempt.Vendor && Checks.NbOnly(k.Value.Spec))).ToDictionary(k => k.Key, k => Checks.Evaluate(k.Value, attempt, transcript, k.Key));
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
    /// null (undecided) when none failed but one could not decide. An undecided run is out of the pass rate on both sides:
    /// a check that cannot decide is the check's weakness, not the variant's.
    /// </summary>
    public static bool? Pass(Dictionary<string, Verdict> checks, List<string> passChecks)
    {
        var results = passChecks.Select(name => checks.TryGetValue(name, out var v) ? v.Result : Verdict.Error).ToList();
        if (results.Any(r => r is Verdict.Fail or Verdict.Error)) return false;
        return results.Any(r => r == Verdict.NeedsJudge) ? null : true;
    }

    /// <summary>Why the run is undecided, or null: the first pass check that needs a judge, as "check: reason", when nothing failed.</summary>
    public static string? Undecided(Dictionary<string, Verdict> checks, List<string> passChecks) =>
        Pass(checks, passChecks) is null
            ? passChecks.Where(name => checks.TryGetValue(name, out var v) && v.Result == Verdict.NeedsJudge).Select(name => $"{name}: {checks[name].Reason}").First()
            : null;

    /// <summary>Why the attempt does not count, or null: the first validity check that failed, as "check: reason". An error is not a fail.</summary>
    public static string? Invalid(Dictionary<string, Verdict> checks, List<string>? validityChecks) =>
        (validityChecks ?? []).Where(name => checks.TryGetValue(name, out var v) && v.Result == Verdict.Fail)
            .Select(name => $"{name}: {checks[name].Reason}").FirstOrDefault();
}
