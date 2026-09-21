using System.Text.Json;
using System.Text.Json.Nodes;

namespace Proctor;

/// <summary>Runs the declared checks over every completed cell and writes checks.json beside the evidence.</summary>
static class Grade
{
    public record CellGrade(string Arm, string Case, int Sample, string Status, Dictionary<string, Verdict>? Checks, bool? Pass, string? Invalid);

    public static List<CellGrade> Experiment(string root, Experiment experiment, Eval eval, TextWriter log)
    {
        var experimentDir = Layout.Experiment(root, experiment.Id);
        var grades = new List<CellGrade>();
        foreach (var (arm, c, sample) in Runner.Cells(eval))
        {
            var cellDir = Layout.Cell(experimentDir, arm.Id!, c.Id!, sample);
            var status = Runner.ReadStatus(cellDir);
            if (status != CellStatus.Completed)
            {
                grades.Add(new CellGrade(arm.Id!, c.Id!, sample, status, null, null, null));
                continue;
            }
            var cell = new CellContext(eval.Dir, cellDir, Layout.Work(root, experiment.Id, arm.Id!, c.Id!, sample), experiment.Id, arm.Id!, c, sample);
            var checks = Cell(cell, eval.Grading, Transcript.Read(cellDir));
            var pass = Pass(checks, eval.Grading.Pass!);
            var invalid = Invalid(checks, eval.Grading.Validity);
            grades.Add(new CellGrade(arm.Id!, c.Id!, sample, status, checks, pass, invalid));
            log.WriteLine($"  {arm.Id}/{c.Id}/{sample}  {(invalid is not null ? "invalid" : pass ? "pass" : "fail")}  {string.Join("  ", checks.Where(k => k.Value.Result != Verdict.Pass).Select(k => $"{k.Key}={k.Value.Result}"))}");
        }
        return grades;
    }

    /// <summary>Grade one cell and write its checks.json. Returns the verdict map.</summary>
    public static Dictionary<string, Verdict> Cell(CellContext cell, Grading grading, Transcript transcript)
    {
        var verdicts = grading.Checks!.ToDictionary(k => k.Key, k => Checks.Evaluate(k.Value, cell, transcript));
        Runner.WriteJson(Path.Combine(cell.CellDir, Layout.ChecksFile), verdicts);
        return verdicts;
    }

    public static Dictionary<string, Verdict>? ReadChecks(string cellDir)
    {
        var file = Path.Combine(cellDir, Layout.ChecksFile);
        return File.Exists(file) ? JsonSerializer.Deserialize<Dictionary<string, Verdict>>(File.ReadAllText(file), Eval.JsonOptions) : null;
    }

    /// <summary>The headline pass: every check named in grading.pass passed. Anything else, including error, is not a pass.</summary>
    public static bool Pass(Dictionary<string, Verdict> checks, List<string> passChecks) =>
        passChecks.All(name => checks.TryGetValue(name, out var v) && v.Result == Verdict.Pass);

    /// <summary>Why the sample does not count, or null: the first validity check that failed, as "check: reason". An error is not a fail.</summary>
    public static string? Invalid(Dictionary<string, Verdict> checks, List<string>? validityChecks) =>
        (validityChecks ?? []).Where(name => checks.TryGetValue(name, out var v) && v.Result == Verdict.Fail)
            .Select(name => $"{name}: {checks[name].Reason}").FirstOrDefault();
}
