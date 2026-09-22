using System.Text.Json;
using System.Text.Json.Nodes;

namespace Proctor;

/// <summary>Runs the declared checks over every completed cell and writes checks.json beside the evidence.</summary>
static class Grade
{
    public record CellGrade(string Arm, string Case, int Sample, string Status, Dictionary<string, Verdict>? Checks, bool? Pass, string? Invalid);

    public static List<CellGrade> Experiment(string root, Experiment experiment, Eval eval, TextWriter log, JudgeClient? judges = null)
    {
        if (judges is { Comparing: true }) return Compare(root, experiment, eval, log, judges);
        var experimentDir = Layout.Experiment(root, experiment.Id);
        var grades = new List<CellGrade>();
        if (judges is not null)
            foreach (var note in Judge.SameFamilyNotes(eval, judges)) log.WriteLine($"  note: {note}");
        foreach (var (arm, c, sample) in Runner.Cells(eval))
        {
            var cellDir = Layout.Cell(experimentDir, arm.Id!, c.Id!, sample);
            var status = Runner.ReadStatus(cellDir);
            if (status != CellStatus.Completed)
            {
                grades.Add(new CellGrade(arm.Id!, c.Id!, sample, status, null, null, null));
                continue;
            }
            var cell = Runner.Context(root, experiment, eval, arm, c, sample) with { Judges = judges };
            // A script check may need the checkout; after it is gone, the fixture plus the diff is the same tree.
            if (cell.Fixture is not null && eval.ChecksFor(c).Values.Any(k => k.Spec.ContainsKey("script")) && Checkout.Restore(cell.Fixture, cell.WorkDir, cellDir) is { } error)
                log.WriteLine($"  {arm.Id}/{c.Id}/{sample}  could not restore the checkout: {error}");
            var checks = Cell(cell, eval, Transcript.Read(cellDir));
            var pass = Pass(checks, eval.Grading.Pass!);
            var invalid = Invalid(checks, eval.Grading.Validity);
            grades.Add(new CellGrade(arm.Id!, c.Id!, sample, status, checks, pass, invalid));
            log.WriteLine($"  {arm.Id}/{c.Id}/{sample}  {(invalid is not null ? "invalid" : pass switch { true => "pass", false => "fail", null => "undecided" })}  {string.Join("  ", checks.Where(k => k.Value.Result != Verdict.Pass).Select(k => $"{k.Key}={k.Value.Result}"))}");
        }
        return grades;
    }

    /// <summary>
    /// A `--judge a=b` pass: only the checks whose declared judge is remapped are evaluated, by b, into b's verdict files
    /// beside a's; checks.json is read, compared against and never written. The grades returned are the ones on file.
    /// </summary>
    static List<CellGrade> Compare(string root, Experiment experiment, Eval eval, TextWriter log, JudgeClient judges)
    {
        var experimentDir = Layout.Experiment(root, experiment.Id);
        var grades = new List<CellGrade>();
        int compared = 0, agreed = 0;
        foreach (var (arm, c, sample) in Runner.Cells(eval))
        {
            var cellDir = Layout.Cell(experimentDir, arm.Id!, c.Id!, sample);
            var status = Runner.ReadStatus(cellDir);
            var existing = status == CellStatus.Completed ? ReadChecks(cellDir) : null;
            if (existing is null)
            {
                grades.Add(new CellGrade(arm.Id!, c.Id!, sample, status, null, null, null));
                if (status == CellStatus.Completed) log.WriteLine($"  {arm.Id}/{c.Id}/{sample}  no checks.json; run grade without --judge first");
                continue;
            }
            var cell = Runner.Context(root, experiment, eval, arm, c, sample) with { Judges = judges };
            var transcript = Transcript.Read(cellDir);
            foreach (var (name, check) in eval.ChecksFor(c))
            {
                if (!Checks.ModelChecks(check.Spec).Any(m => judges.Remaps(m.Name, m.Spec, name))) continue;
                var (from, to) = Checks.ModelChecks(check.Spec).Select(m => (judges.Declared(Judge.KindOf(m.Name), (m.Spec["with"] as JsonValue)?.GetValue<string>(), name).Name, judges.Resolve(Judge.KindOf(m.Name), (m.Spec["with"] as JsonValue)?.GetValue<string>(), name).Name)).First();
                var other = Checks.Evaluate(check, cell, transcript, name);
                var applied = existing.GetValueOrDefault(name);
                compared++;
                if (applied?.Result == other.Result) agreed++;
                log.WriteLine($"  {arm.Id}/{c.Id}/{sample}  {name}: {from}={applied?.Result ?? "none"} ({applied?.Reason})  {to}={other.Result} ({other.Reason})");
            }
            grades.Add(new CellGrade(arm.Id!, c.Id!, sample, status, existing, Pass(existing, eval.Grading.Pass!), Invalid(existing, eval.Grading.Validity)));
        }
        log.WriteLine($"  {agreed} of {compared} verdicts agree; checks.json unchanged");
        return grades;
    }

    /// <summary>Grade one cell and write its checks.json. Returns the verdict map.</summary>
    public static Dictionary<string, Verdict> Cell(CellContext cell, Eval eval, Transcript transcript)
    {
        var verdicts = eval.ChecksFor(cell.Case).ToDictionary(k => k.Key, k => Checks.Evaluate(k.Value, cell, transcript, k.Key));
        Runner.WriteJson(Path.Combine(cell.CellDir, Layout.ChecksFile), verdicts);
        return verdicts;
    }

    public static Dictionary<string, Verdict>? ReadChecks(string cellDir)
    {
        var file = Path.Combine(cellDir, Layout.ChecksFile);
        return File.Exists(file) ? JsonSerializer.Deserialize<Dictionary<string, Verdict>>(File.ReadAllText(file), Eval.JsonOptions) : null;
    }

    /// <summary>
    /// The headline pass: true when every check named in grading.pass passed; false when any failed, errored or is missing;
    /// null (undecided) when none failed but one could not decide. An undecided run is out of the pass rate on both sides:
    /// a check that cannot decide is the check's weakness, not the arm's.
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

    /// <summary>Why the sample does not count, or null: the first validity check that failed, as "check: reason". An error is not a fail.</summary>
    public static string? Invalid(Dictionary<string, Verdict> checks, List<string>? validityChecks) =>
        (validityChecks ?? []).Where(name => checks.TryGetValue(name, out var v) && v.Result == Verdict.Fail)
            .Select(name => $"{name}: {checks[name].Reason}").FirstOrDefault();
}
