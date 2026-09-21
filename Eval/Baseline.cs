using System.Text.Json;

namespace Proctor;

// evals/<eval>/baseline.json: pinned cells per case, "what we believe is achievable right now".
// The cells are the truth and the score is a cache: when the pinned experiment is still under
// runs/, the report re-reads its checks.json; when it is gone, the pinned score stands.

record BaselinePin(string Experiment, string Arm, List<int> Samples, string? Bundle, string? Fixture, double Score);

record Baseline(string Set, string Command, string EvalHash, Dictionary<string, BaselinePin> Cases)
{
    public static string FileOf(Eval eval) => Path.Combine(eval.Dir, Layout.BaselineFile);

    public static Baseline? Load(Eval eval)
    {
        var file = FileOf(eval);
        return File.Exists(file) ? JsonSerializer.Deserialize<Baseline>(File.ReadAllText(file), Eval.JsonOptions) : null;
    }

    public void Save(Eval eval) => Runner.WriteJson(FileOf(eval), this);

    /// <summary>Per-case scores for the report: recomputed from the pinned cells where they still exist, else as pinned. The word says which.</summary>
    public (Dictionary<string, double> Scores, string Source) Resolve(string root, Eval eval)
    {
        var scores = new Dictionary<string, double>();
        int recomputed = 0, pinned = 0;
        foreach (var (caseId, pin) in Cases)
        {
            var cells = pin.Samples.Select(s => Layout.Cell(Layout.Experiment(root, pin.Experiment), pin.Arm, caseId, s)).ToList();
            var verdicts = cells.Select(Grade.ReadChecks).ToList();
            if (verdicts.All(v => v is not null))
            {
                var counted = verdicts.Where(v => Grade.Invalid(v!, eval.Grading.Validity) is null).ToList();
                if (counted.Count > 0)
                {
                    scores[caseId] = counted.Average(v => Grade.Pass(v!, eval.Grading.Pass!) ? 1.0 : 0.0);
                    recomputed++;
                    continue;
                }
            }
            scores[caseId] = pin.Score;
            pinned++;
        }
        return (scores, recomputed == 0 ? "as pinned" : pinned == 0 ? "recomputed" : "partly recomputed");
    }
}
