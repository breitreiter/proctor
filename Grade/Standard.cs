using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Proctor;

// Grades over time (plans/axes-build.md, slice 5; plans/axes.md, "Grades over time"). The checks are the standard, and
// the standard is allowed to change after the tries are made: a check added later is graded over every earlier try, and
// a check narrowed to some values is withdrawn from the others. So a grade names the standard it was made under, and a
// regrade under another adds a record beside the old one. What reads results uses the current standard, both sides
// alike; what moved between an earlier grade and this one is said, since a number can change with nothing re-run.

/// <summary>One grade of one stage, kept in the attempt under grades/: the standard it was made under, when, and every verdict.</summary>
record GradeRecord(string Standard, string ExamHash, string Graded, Dictionary<string, Verdict> Checks);

/// <summary>
/// A check whose standing changed between an earlier grade of these tries and the current one, with nothing re-run. Kind
/// is narrowed (it no longer applies where it was decided), added (it did not exist when the tries were first graded) or
/// removed. Passed and Failed count the verdicts concerned: for narrowed and removed, as they were; for added, as they are.
/// </summary>
record StandardMove(string Check, string Kind, int Stages, int Passed, int Failed, List<string> Configurations, string Sentence);

static class Standard
{
    /// <summary>
    /// The standard a stage is graded under: every check it carries (its fields, the condition it applies under, a
    /// script's content), which of them decide correctness and which integrity, and the expected values. Not the
    /// prompt, the fixture or anything else the agent meets, which are the task's hash.
    /// </summary>
    public static string Hash(Exam exam, TaskDef stage)
    {
        var parts = new List<string>();
        foreach (var (name, check) in exam.ChecksFor(stage).OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            parts.Add($"{name} {check.Spec.ToJsonString()}");
            if (check.Spec["script"] is JsonValue v && v.TryGetValue<string>(out var script) && Path.Combine(check.Dir, script) is var file && File.Exists(file))
                parts.Add("  " + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))));
        }
        parts.Add("correctness " + string.Join(",", exam.CorrectnessFor(stage)));
        parts.Add("integrity " + string.Join(",", exam.IntegrityFor(stage) ?? []));
        parts.Add("quality " + string.Join(",", (exam.Grading.Quality ?? []).Where(exam.ChecksFor(stage).ContainsKey)));
        parts.Add("expect " + (exam.ExpectFor(stage)?.ToJsonString() ?? ""));
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", parts))));
    }

    /// <summary>The condition a check applies under (its `when`), or null when it applies everywhere.</summary>
    public static When? WhenOf(JsonObject spec) =>
        spec[Checks.When] is JsonValue v && v.TryGetValue<string>(out var text) ? When.Parse(text, out _) : null;

    /// <summary>Whether a check applies in a configuration. For a check without a condition, always.</summary>
    public static bool Applies(Exam exam, CheckDef check, string configuration) =>
        WhenOf(check.Spec) is not { } when || exam.Axes.Of(configuration) is not { } c || when.Holds(c);

    /// <summary>The correctness checks of a stage that apply in a configuration: what a try of it can hold.</summary>
    public static List<string> PassIn(Exam exam, TaskDef stage, string configuration)
    {
        var checks = exam.ChecksFor(stage);
        return exam.CorrectnessFor(stage).Where(name => Applies(exam, checks[name], configuration)).ToList();
    }

    static string File_(string attemptDir, string standard) => Path.Combine(attemptDir, Layout.GradesDir, standard[7..23] + ".json");

    /// <summary>Keep a grade under the standard it was made under. A regrade under the same standard replaces its record; under another it adds one.</summary>
    public static void Keep(string attemptDir, Exam exam, TaskDef stage, Dictionary<string, Verdict> verdicts)
    {
        var standard = Hash(exam, stage);
        Directory.CreateDirectory(Path.Combine(attemptDir, Layout.GradesDir));
        Runner.WriteJson(File_(attemptDir, standard), new GradeRecord(standard, exam.Hash, DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"), verdicts));
    }

    /// <summary>Whether the stage holds a grade under the standard as it is now. Without one it is not yet graded, whatever an earlier grade said.</summary>
    public static bool Current(Exam exam, TaskDef stage, string attemptDir) => File.Exists(File_(attemptDir, Hash(exam, stage)));

    /// <summary>Every grade the stage holds, oldest first.</summary>
    public static List<GradeRecord> History(string attemptDir)
    {
        var dir = Path.Combine(attemptDir, Layout.GradesDir);
        if (!Directory.Exists(dir)) return [];
        return Directory.GetFiles(dir, "*.json").Select(f => JsonSerializer.Deserialize<GradeRecord>(File.ReadAllText(f), Exam.JsonOptions)).OfType<GradeRecord>()
            .OrderBy(r => r.Graded, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// What moved between an earlier grade of these stages and the current one. A check is narrowed where it was decided
    /// before and does not apply now; added where the stage's first grade did not have it; removed where the grade before
    /// this one had it and this one does not. Only stages graded more than once, and graded under the current standard, say anything.
    /// </summary>
    public static List<StandardMove> Moves(string root, Exam exam, IEnumerable<BankAttempt> attempts)
    {
        static bool Decided(Verdict? v) => v?.Result is Verdict.Pass or Verdict.Fail;
        var found = new List<(string Check, string Kind, string Configuration, bool Passed)>();
        foreach (var a in attempts)
        {
            var stage = exam.Tasks.FirstOrDefault(t => t.Id == a.Task);
            if (stage is null) continue;
            var history = History(a.Dir(root));
            var standard = Hash(exam, stage);
            var at = history.FindIndex(r => r.Standard == standard);
            if (at < 1) continue;
            var (now, first, before) = (history[at], history[0], history[at - 1]);
            foreach (var (name, verdict) in now.Checks)
            {
                if (verdict.Result == Verdict.NotApplicable && history.Take(at).LastOrDefault(r => Decided(r.Checks.GetValueOrDefault(name))) is { } then)
                    found.Add((name, "narrowed", a.Configuration, then.Checks[name].Result == Verdict.Pass));
                else if (!first.Checks.ContainsKey(name) && Decided(verdict))
                    found.Add((name, "added", a.Configuration, verdict.Result == Verdict.Pass));
            }
            foreach (var (name, verdict) in before.Checks)
                if (!now.Checks.ContainsKey(name) && Decided(verdict)) found.Add((name, "removed", a.Configuration, verdict.Result == Verdict.Pass));
        }
        return found.GroupBy(f => (f.Check, f.Kind)).Select(g =>
        {
            var (n, passed, failed) = (g.Count(), g.Count(f => f.Passed), g.Count(f => !f.Passed));
            var configurations = g.Select(f => f.Configuration).Distinct().ToList();
            var where = $" in {string.Join("; ", configurations.Select(key => exam.Axes.Of(key) is { } c ? c.Name ?? c.Label : key))}";
            var stages = $"{n} stage{(n == 1 ? "" : "s")}";
            var sentence = g.Key.Kind switch
            {
                "narrowed" => $"{g.Key.Check} no longer applies{where}: {(n == 1 ? "1 verdict was" : $"{n} verdicts were")} withdrawn ({failed} failed, {passed} passed). Nothing was re-run; these numbers moved because the standard did.",
                "added" when failed == 0 => $"{g.Key.Check} was added after {stages}{where} had first been graded, and passes in all of them.",
                "added" => $"{g.Key.Check} was added after {stages}{where} had first been graded: it fails in {failed} and passes in {passed}. The failures are faults found later in tries already made, not a value getting worse.",
                _ => $"{g.Key.Check} was removed after it had graded {stages}{where} ({failed} failed, {passed} passed). Nothing was re-run; these numbers moved because the standard did.",
            };
            return new StandardMove(g.Key.Check, g.Key.Kind, n, passed, failed, configurations, sentence);
        }).OrderBy(m => m.Check, StringComparer.Ordinal).ThenBy(m => m.Kind, StringComparer.Ordinal).ToList();
    }
}
