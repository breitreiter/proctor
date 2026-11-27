using System.Globalization;
using System.Text.Json;
using P = Proctor;

namespace Auspex;

/// <summary>
/// What only proctor may work out, asked of proctor's own code: an exam's axes and the configurations it carries, where
/// it has tries (coverage), and one change against another (a comparison). The viewer renders these and computes none
/// of them, so a number on a page is the number `proctor compare` gives (proctor's plans/axes-build.md, slices 3c and
/// 3d). Nothing here calls a model: a hosted viewer has no key, and needs none.
/// </summary>
static class Measured
{
    /// <summary>The exam as proctor loads it, when it declares axes; otherwise why not, in proctor's words.</summary>
    public static (P.Exam? Exam, string? Problem) Load(string root, string id)
    {
        var problems = new List<P.Problem>();
        try
        {
            var exam = P.Exam.Load(root, id, problems);
            return exam?.Axes is not null ? (exam, null) : (null, problems.Count > 0 ? string.Join("\n", problems) : "this exam declares no axes");
        }
        catch (Exception e) when (e is P.ProctorException or IOException or JsonException or UnauthorizedAccessException) { return (null, e.Message); }
    }

    public static (P.CoverageAnswer? Answer, string? Problem) Coverage(string root, P.Exam exam, string? asOf) =>
        Asked(() => P.Answers.Coverage(root, exam, asOf is null ? null : P.Answers.ParseAsOf(asOf)));

    /// <summary>
    /// A comparison by its address: `change` and `against` as axis=value lists, `within` likewise, `as_of`; or
    /// `configuration` (a name or a key) with an optional `against` configuration, as `proctor compare --configuration` takes it.
    /// </summary>
    public static (P.ComparisonAnswer? Answer, string? Problem) Compare(string root, P.Exam exam, Func<string, string?> query, bool now = false)
    {
        var axes = exam.Axes!;
        return Asked(() =>
        {
            DateTime? asOf = now || query("as_of") is not { Length: > 0 } text ? null : P.Answers.ParseAsOf(text);
            var within = Values(query("within"));
            if (query("configuration") is { Length: > 0 } asked)
            {
                P.Configuration Named(string given) => axes.Named.FirstOrDefault(c => c.Name == given) ?? axes.Of(given) ?? throw new P.ProctorException($"'{given}' is not a configuration of this exam");
                return P.Answers.Compare(root, exam, Named(asked), query("against") is { Length: > 0 } other ? Named(other) : null, within.Count > 0 ? within : null, asOf);
            }
            var (change, against) = (Values(query("change")), Values(query("against")));
            if (change.Count == 0 || !change.Keys.Order().SequenceEqual(against.Keys.Order())) throw new P.ProctorException("a comparison names what changed and what it is compared with, on the same axes: ?change=axis=value&against=axis=value");
            foreach (var (axis, value) in change.Concat(against).Concat(within))
                if (!axes.Def.TryGetValue(axis, out var def) || !def.Values!.ContainsKey(value)) throw new P.ProctorException($"{axis}={value} is not a value this exam declares");
            return P.Answers.Compare(root, exam, change, against, within.Count > 0 ? within : null, asOf, null);
        });
    }

    /// <summary>
    /// An exam's report as `proctor report` computes it, from the bank as it is now: its named configurations with their
    /// tries, its stages and each check's role, its predictions and how each change came out, what is lost.
    /// </summary>
    public static (P.ConfigurationsFile? Stats, string? Problem) Stats(string root, P.Exam exam) => Asked(() =>
    {
        var current = P.Bank.Select(root, exam).Current;
        var problems = new List<P.Problem>();
        var predictions = P.Predictions.Load(root, exam, problems);
        if (problems.Count > 0) throw new P.ProctorException(string.Join("\n", problems));
        return P.Configurations.Compute(root, exam, current, P.Bank.Of(root, exam), P.Results.Collect(root, exam, current), null, predictions).Stats;
    });

    static (T? Answer, string? Problem) Asked<T>(Func<T> ask) where T : class
    {
        try { return (ask(), null); }
        catch (Exception e) when (e is P.ProctorException or IOException or JsonException or UnauthorizedAccessException) { return (null, e.Message); }
    }

    /// <summary>axis=value,axis=value as a map; empty when there is none.</summary>
    static Dictionary<string, string> Values(string? list) =>
        (list ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1]);

    public static string PValue(double p) => p < 0.001 ? "<0.001" : p.ToString("0.###", CultureInfo.InvariantCulture);
}
