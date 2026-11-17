using System.Text.Json;

namespace Proctor;

/// <summary>
/// exams/&lt;exam&gt;/live.json: the variant in use now, by its id and the hash it had when it was marked, and since when.
/// It is the reference the report compares the other variants with. Committed with the exam; absent until one is picked.
/// </summary>
record Live(Ref Variant, string Since)
{
    public static string FileOf(Exam exam) => Path.Combine(exam.Dir, Layout.LiveFile);

    /// <summary>The mark, or null when nothing is live. A file that is not a mark is the user's to fix, said plainly.</summary>
    public static Live? Load(Exam exam)
    {
        var file = FileOf(exam);
        if (!File.Exists(file)) return null;
        try
        {
            return Runner.ReadJson<Live>(file) is { Variant: { Id: not null, Hash: not null } } live
                ? live : throw new ProctorException($"{file}: not a live mark; it holds {{\"variant\": {{\"id\", \"hash\"}}, \"since\"}}, as `proctor live` writes it");
        }
        catch (JsonException e)
        {
            throw new ProctorException($"{file}: not JSON ({e.Message.Split(" Path:")[0]})");
        }
    }

    public void Save(Exam exam) => Runner.WriteJson(FileOf(exam), this);

    /// <summary>Why the mark no longer holds, as a clause about the variant: it has no file, or its file has changed since it was marked. Null when it holds.</summary>
    public string? Broken(Exam exam) => exam.Variants.FirstOrDefault(v => v.Id == Variant.Id) switch
    {
        null => "it has no file under variants/",
        { } v when v.Hash != Variant.Hash => $"its file has changed since it was marked ({Bank.Short(Variant.Hash)} then, {Bank.Short(v.Hash)} now)",
        _ => null,
    };

    /// <summary>A broken mark as one line for list, run and report, saying what to do.</summary>
    public string Warning(Exam exam, string why) =>
        $"warning: {Layout.ExamsDir}/{exam.Id}/{Layout.LiveFile} marks {Variant.Id} live, but {why}; nothing is treated as live until `proctor live {exam.Id} <variant>` marks one";
}
