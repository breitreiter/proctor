namespace Proctor;

/// <summary>
/// The report's stamp: when it was built, the log entries of the runs that made its tries, each configuration's and
/// stage's hash as the exam has them now, how many current stages it read and how many stale ones it left out (each
/// with why), the narrowing asked for, and, per configuration, the conditions its tries were made under when they were
/// not all the same.
/// </summary>
record BuiltFrom(string At, List<string> Log, Dictionary<string, string> Configurations, Dictionary<string, string> Tasks, int Current, int Stale,
    List<StaleAttempt>? StaleAttempts = null, string? Narrowed = null, Dictionary<string, List<string>>? Mixed = null);

static class Stamp
{
    /// <summary>
    /// The stamp, from the current stages the report read and the stale ones it left out. A configuration's conditions
    /// are its runs' versions, runner and engine, as their log entries recorded them; it is mixed when its tries came
    /// from runs that differ in one.
    /// </summary>
    public static BuiltFrom Of(Exam exam, BankSelection selection, string? narrowed, DateTime at)
    {
        var current = selection.Current;
        var mixed = new Dictionary<string, List<string>>();
        foreach (var assistant in exam.Assistants.Where(v => current.Any(a => a.Configuration == v.Id)))
        {
            var conditions = current.Where(a => a.Configuration == assistant.Id).Select(a => a.Entry).DistinctBy(b => b.Id).Select(b => Conditions(b, assistant.IsAgent)).ToList();
            var differ = conditions.SelectMany(c => c.Keys).Distinct()
                .Select(k => (k, values: conditions.Select(c => c.GetValueOrDefault(k, "none")).Distinct().Order(StringComparer.Ordinal).ToList()))
                .Where(x => x.values.Count > 1).Select(x => $"{x.k} {string.Join(", ", x.values)}").ToList();
            if (differ.Count > 0) mixed[assistant.Id!] = differ;
        }
        return new BuiltFrom(at.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            current.Select(a => a.Entry.Id).Distinct().Order(StringComparer.Ordinal).ToList(),
            exam.Assistants.Where(v => current.Any(a => a.Configuration == v.Id)).ToDictionary(v => v.Id!, v => v.Hash),
            exam.Stages.Where(t => current.Any(a => a.Stage == t.Id)).ToDictionary(t => t.Id!, t => exam.StageHashes[t.Id!]),
            current.Count, selection.Stale.Count, selection.Stale.Count > 0 ? selection.Stale : null, narrowed, mixed.Count > 0 ? mixed : null);

        // Harbor's version and engine are a condition only where the tries went through Harbor; a bare configuration's run may or may not have had Harbor beside it.
        static Dictionary<string, string> Conditions(LogEntry b, bool agent)
        {
            var c = new Dictionary<string, string>(b.Versions) { ["runner"] = b.Nb.Runner is { } r ? $"{r.Script} {r.Hash[..Math.Min(r.Hash.Length, 15)]}" : "none" };
            if (agent && b.Harbor is { } h) c["engine"] = h.Engine;
            else c.Remove("harbor");
            return c;
        }
    }
}
