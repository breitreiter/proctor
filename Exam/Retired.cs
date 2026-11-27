using System.Text.Json;

namespace Proctor;

/// <summary>One retired configuration: why, in its author's words, and since when.</summary>
record RetiredMark(string Reason, string Since);

/// <summary>The file: its schema name and number, and the retired configurations by name.</summary>
record RetiredFile(string Schema, Dictionary<string, RetiredMark> Configurations);

/// <summary>
/// exams/&lt;exam&gt;/retired.json on an exam with axes: the named configurations nobody is looking at any more, each with
/// its reason and the date (the user, 2026-11-27), as schema `retired/1`. Written by `proctor retire`, committed with the exam and in no hash.
/// Retiring deletes nothing: the tries, the predictions and the comparisons stay, and the configuration can be brought
/// back. What it changes: `run` makes no new tries of it, and the main lists leave it out.
/// </summary>
static class Retired
{
    public const string Schema = "retired/1";

    public static string FileOf(Exam exam) => Path.Combine(exam.Dir, Layout.RetiredFile);

    /// <summary>The retired configurations by name; empty when there is no file.</summary>
    public static Dictionary<string, RetiredMark> Load(Exam exam)
    {
        var file = FileOf(exam);
        if (!File.Exists(file)) return [];
        try
        {
            var read = JsonSerializer.Deserialize<RetiredFile>(File.ReadAllText(file), Exam.JsonOptions);
            return read is { Schema: Schema, Configurations: { } marks } && marks.All(m => m.Value is { Reason: not null, Since: not null }) ? marks
                : throw new ProctorException($"{file}: not the file `proctor retire` writes; it holds {{\"schema\": \"{Schema}\", \"configurations\": {{\"<name>\": {{\"reason\", \"since\"}}}}}}");
        }
        catch (JsonException e)
        {
            throw new ProctorException($"{file}: not JSON ({e.Message.Split(" Path:")[0]})");
        }
    }

    /// <summary>Written whole; with nothing retired the file goes, so an exam that retires nothing has none.</summary>
    public static void Save(Exam exam, Dictionary<string, RetiredMark> marks)
    {
        if (marks.Count == 0) File.Delete(FileOf(exam));
        else Runner.WriteJson(FileOf(exam), new RetiredFile(Schema, marks.OrderBy(m => m.Key, StringComparer.Ordinal).ToDictionary(m => m.Key, m => m.Value)));
    }
}
