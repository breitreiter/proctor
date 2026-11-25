using System.Globalization;

namespace Proctor;

// How each change a prediction expects came out (plans/axes-build.md, slice 6). A prediction is held to account by the
// same measure every comparison uses, try against try: the prediction's tries against those of what it is compared with.
// A miss is said as plainly as a hit, and so is a prediction that so few tries cannot settle.

/// <summary>
/// One expected change's outcome. Outcome is held (as predicted, confirmed), leaning (as predicted, not confirmed),
/// no_difference, opposite (the other way, not confirmed), opposite_confirmed, or not_run (a side has no graded try).
/// </summary>
record ChangeOutcome(string? Check, string? Metric, string Direction, string? Because, string Outcome, int Tries, int AgainstTries, Measure? Measure, bool CouldConfirm, string Sentence);

/// <summary>
/// A prediction with each change it expects held against the tries. Late: some of its configuration's tries were made
/// before it was pinned. StandardChanged: the checks of one of its stages are not those it was pinned under.
/// </summary>
record PredictionReport(string Name, string Configuration, string Against, string? Note, string? Written, bool Late, bool StandardChanged, List<string> Labels, List<ChangeOutcome> Changes);

static class PredictionOutcomes
{
    const double Alpha = 0.05;

    public static List<PredictionReport> Compute(Exam exam, List<Prediction> predictions, IReadOnlyDictionary<string, TryVariant> byKey, List<ResultRow> rows, IEnumerable<BankAttempt> current)
    {
        var made = current.Select(a => (a.Variant, Made: Answers.MadeAt(a.Entry.IdOf(a.Variant, a.Attempt)), a.Attempt)).Where(x => x.Made is not null).Distinct().ToList();
        return predictions.Select(c =>
        {
            var (mine, theirs) = (byKey.GetValueOrDefault(c.Configuration.Key), byKey.GetValueOrDefault(c.Against.Key));
            var (name, other) = (c.Configuration.Name ?? c.Configuration.Label, c.Against.Name ?? c.Against.Label);
            var labels = new List<string>();
            var written = c.Def.Written is null ? (DateTime?)null : Answers.ParseAsOf(c.Def.Written);
            var before = written is null ? 0 : made.Count(m => m.Variant == c.Configuration.Key && m.Made < written);
            if (written is null) labels.Add($"Not pinned: when this prediction was written is not recorded, so it cannot be said that it came before the tries. `proctor predict {exam.Id} {c.Name}` pins it.");
            else if (before > 0) labels.Add($"Late: {before} {(before == 1 ? "try" : "tries")} of {name} had been made before this prediction was pinned ({c.Def.Written}).");
            var moved = c.Def.Standard is { } pinned && exam.StagesOf(c.Configuration.Key).Any(s => pinned.GetValueOrDefault(s.Id!) != Standard.Hash(exam, s));
            if (moved) labels.Add("The standard changed after this prediction was pinned: the checks it is held against are not the checks it was written under.");

            var outcomes = (c.Def.Changes ?? []).Select(p =>
            {
                var said = Predictions.Said(p, c);
                List<double>? Numbers(TryVariant? v, string key) => v?.Rows.Where(r => r.Invalid is null).Select(r => Number(p, r, key, rows)).ToList() is { } all && all.All(n => n is not null) ? all.Select(n => n!.Value).ToList() : null;
                var (a, b) = (Numbers(mine, c.Configuration.Key), Numbers(theirs, c.Against.Key));
                if (a is null or { Count: 0 } || b is null or { Count: 0 })
                    return new ChangeOutcome(p.Check, p.Metric, p.Direction!, p.Because, "not_run", a?.Count ?? 0, b?.Count ?? 0, null, false,
                        $"Predicted {said}: not run yet ({(a is null or { Count: 0 } ? name : other)} has no graded try{(p.Metric == "flagged_steps" && (a is null || b is null) ? ", or friction has not been run on every stage" : "")}).");
                var measure = Tries.Compare(a, b, higherIsBetter: true);   // "better" here only means higher
                var could = Tries.MinP(a.Count, b.Count) < Alpha;
                var went = measure.Direction == "same" ? null : measure.Direction == "better" ? Predictions.More : Predictions.Less;
                var outcome = went is null ? "no_difference" : went == p.Direction ? (measure.Confirmed ? "held" : "leaning") : measure.Confirmed ? "opposite_confirmed" : "opposite";
                var tries = $"{a.Count} against {b.Count} {(a.Count + b.Count == 2 ? "try" : "tries")}";
                var strength = measure.Confirmed ? $"confirmed (p = {P(measure.P)})" : could ? $"not confirmed (p = {P(measure.P)})" : $"which {tries} cannot confirm (p = {P(measure.P)})";
                var sentence = outcome switch
                {
                    "no_difference" => $"Predicted {said}: it did not happen. No difference in {tries}{(could ? "" : ", which are too few to show one")}.",
                    "held" or "leaning" => $"Predicted {said}: it went that way, {strength}.",
                    _ => $"Predicted {said}: it went the other way, {strength}.",
                };
                return new ChangeOutcome(p.Check, p.Metric, p.Direction!, p.Because, outcome, a.Count, b.Count, measure, could, sentence);
            }).ToList();
            return new PredictionReport(c.Name, c.Configuration.Key, c.Against.Key, c.Def.Note, c.Def.Written, before > 0, moved, labels, outcomes);
        }).ToList();
    }

    /// <summary>The number a change is about, for one try: the stages in which its check held, or the try's own count. Null when it cannot be read (friction not run).</summary>
    static double? Number(ChangeDef p, TryRow row, string key, List<ResultRow> rows) => p.Check is { } check
        ? rows.Count(r => r.Variant == key && r.Attempt == row.N && r.Checks?.GetValueOrDefault(check) == Verdict.Pass)
        : p.Metric switch
        {
            "held" => row.Held, "flagged_steps" => row.Flagged, "tool_calls" => row.ToolCalls, "tokens" => row.TokensIn, "duration" => row.DurationMs, _ => null,
        };

    static string P(double p) => p < 0.001 ? "<0.001" : p.ToString("0.###", CultureInfo.InvariantCulture);
}
