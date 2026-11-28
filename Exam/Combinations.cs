using System.Text.Json;

namespace Proctor;

// exams/<exam>/combinations.json (plans/axes-build.md, slice 3b; plans/axes.md, decision 16). Committed with the exam
// and in no hash. What `proctor build` found when it held each configuration of several values
// against those values built alone. It is a measurement, written by build and by nothing else. Whether anyone read
// what a value changes is not recorded anywhere: a record of that would either pass for an approval or mean nothing.

/// <summary>
/// One configuration built and held against its values built alone. Differs lists the paths where it is not what they
/// add up to, which means some script reads more than one axis. Unclear lists the paths several of its values change
/// in the same lines, where adding up has no meaning. Both empty: it is exactly its values put together.
/// </summary>
record CombinationCheck(string Configuration, string Hash, string Base, List<string> Differs, string Checked, List<string>? Unclear = null);

record CombinationsFile(List<CombinationCheck>? Combinations = null);

/// <summary>An exam's combination checks as read: what holds today, by the hashes each was made under.</summary>
sealed class Combinations
{
    public const string Sum = "sum", More = "more", Unclear = "unclear", NotChecked = "not_checked";

    public List<CombinationCheck> Checks { get; init; } = [];

    public static string FileOf(Exam exam) => Path.Combine(exam.Dir, Layout.CombinationsFile);

    public static Combinations Load(Exam exam)
    {
        var file = FileOf(exam);
        if (!File.Exists(file)) return new Combinations();
        try
        {
            return new Combinations { Checks = JsonSerializer.Deserialize<CombinationsFile>(File.ReadAllText(file), Exam.JsonOptions)?.Combinations ?? [] };
        }
        catch (JsonException e)
        {
            throw new ProctorException($"{Path.Combine("exams", exam.Id, Layout.CombinationsFile)}: not the file `proctor build` writes ({e.Message}); delete it and build again");
        }
    }

    public void Save(Exam exam) => Runner.WriteJson(FileOf(exam), new CombinationsFile(Checks));

    /// <summary>Record a check, in place of any earlier one of the same configuration. False when nothing changed, so the file is not rewritten for a date.</summary>
    public bool Add(CombinationCheck check)
    {
        if (Checks.LastOrDefault(k => k.Configuration == check.Configuration) is { } was && was.Hash == check.Hash && was.Base == check.Base
            && was.Differs.SequenceEqual(check.Differs) && (was.Unclear ?? []).SequenceEqual(check.Unclear ?? [])) return false;
        Checks.RemoveAll(k => k.Configuration == check.Configuration);
        Checks.Add(check);
        return true;
    }

    /// <summary>
    /// How each configuration the exam carries stands today, by key, when it is more than one value off the reference:
    /// its values put together (sum), more than that (more: some script reads several axes), not to be told from its
    /// values (unclear), or never checked as it and the base are now (not_checked). Where some axis has no reference
    /// nothing can be built alone, so nothing is checked.
    /// </summary>
    public Dictionary<string, (string State, List<string> Paths)> Stand(Exam exam)
    {
        var reference = Build.ReferenceOrNull(exam);
        var @base = reference is null ? null : Build.BaseHash(exam, reference);
        var standing = new Dictionary<string, (string, List<string>)>();
        foreach (var c in exam.Axes!.Configurations)
        {
            if (reference is not null && Build.Off(c, reference).Count < 2) continue;
            var check = Checks.LastOrDefault(k => k.Configuration == c.Key && k.Hash == c.Hash && k.Base == @base);
            standing[c.Key] = check is null ? (NotChecked, []) : check.Differs.Count > 0 ? (More, check.Differs) : check.Unclear is { Count: > 0 } ? (Unclear, check.Unclear) : (Sum, []);
        }
        return standing;
    }
}
