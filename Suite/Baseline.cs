using System.Text.Json;

namespace Proctor;

// suites/<suite>/baseline.json: pinned cells per task, "what we believe is achievable right now".
// The cells are the truth and the score is a cache: when the pinned experiment is still under
// runs/, the report re-reads its checks.json; when it is gone, the pinned score stands.

record BaselinePin(string Experiment, string Arm, List<int> Samples, string? Bundle, string? Fixture, double Score);

/// <summary>A headline check the suite requires that a pinned experiment's cells were never graded on, and the tasks it left as pinned.</summary>
record BaselineGap(string Check, string Experiment, List<string> Tasks);

record Baseline(string Set, string Command, string SuiteHash, Dictionary<string, BaselinePin> Tasks)
{
    public static string FileOf(Suite suite) => Path.Combine(suite.Dir, Layout.BaselineFile);

    public static Baseline? Load(Suite suite)
    {
        var file = FileOf(suite);
        return File.Exists(file) ? Runner.ReadJson<Baseline>(file, ("eval_hash", "suite_hash"), ("cases", "tasks")) : null;
    }

    public void Save(Suite suite) => Runner.WriteJson(FileOf(suite), this);

    /// <summary>
    /// Per-task scores for the report: recomputed from the pinned cells where they still exist, else as pinned. The word says which.
    /// A pinned cell graded without one of the suite's pass checks cannot be rescored under the current list (a missing check
    /// would read as a fail and score 0), so its task stands as pinned and the check is returned as a gap.
    /// </summary>
    public (Dictionary<string, double> Scores, string Source, List<BaselineGap> Gaps) Resolve(string root, Suite suite)
    {
        var scores = new Dictionary<string, double>();
        var gaps = new List<BaselineGap>();
        int recomputed = 0, pinned = 0;
        foreach (var (taskId, pin) in Tasks)
        {
            var cells = pin.Samples.Select(s => Layout.Cell(Layout.Experiment(root, pin.Experiment), pin.Arm, taskId, s)).ToList();
            var verdicts = cells.Select(Grade.ReadChecks).ToList();
            if (verdicts.All(v => v is not null))
            {
                var missing = suite.Grading.Pass!.Where(name => verdicts.Any(v => !v!.ContainsKey(name))).ToList();
                foreach (var name in missing)
                {
                    var gap = gaps.FirstOrDefault(g => g.Check == name && g.Experiment == pin.Experiment);
                    if (gap is null) gaps.Add(gap = new BaselineGap(name, pin.Experiment, []));
                    gap.Tasks.Add(taskId);
                }
                var decided = missing.Count > 0 ? [] : verdicts.Where(v => Grade.Invalid(v!, suite.Grading.Validity) is null && Grade.Pass(v!, suite.Grading.Pass!) is not null).ToList();
                if (decided.Count > 0)
                {
                    scores[taskId] = decided.Average(v => Grade.Pass(v!, suite.Grading.Pass!) == true ? 1.0 : 0.0);
                    recomputed++;
                    continue;
                }
            }
            scores[taskId] = pin.Score;
            pinned++;
        }
        return (scores, recomputed == 0 ? "as pinned" : pinned == 0 ? "recomputed" : "partly recomputed", gaps);
    }
}
