using System.Text.Json;
using System.Text.Json.Nodes;

namespace Auspex;

/// <summary>
/// What proctor wrote under its root, read from disk and nothing more: the exams in bank/, each attempt's files, and
/// from exams/&lt;exam&gt;/ the exam's description and its live mark. An attempt is a task's
/// (bank/&lt;exam&gt;/&lt;variant&gt;/&lt;task&gt;/&lt;n&gt;/) or, on an exam of stages, one stage of a try
/// (bank/&lt;exam&gt;/&lt;variant&gt;/&lt;n&gt;/stages/&lt;i&gt;-&lt;id&gt;/); which it is shows on disk, by the stages directory.
/// Proctor's file names are kept here, in one place, so they are easy to find when auspex folds into proctor.
/// Nothing is computed: whether an attempt is current (its hashes) is proctor's to say, and this does not guess it.
/// </summary>
public static class Bank
{
    const string BankDir = "bank", ExamsDir = "exams";
    const string ExamFile = "exam.json", LiveFile = "live.json";
    const string ManifestFile = "manifest.json", StatusFile = "status", TrajectoryFile = "trajectory.json";
    const string ChecksFile = "checks.json", FrictionFile = "friction.json", CoreFile = "core.json";
    const string StagesDir = "stages";

    /// <summary>The exams that have a bank, by id.</summary>
    public static IReadOnlyList<string> Exams(string root) =>
        Directory.Exists(Path.Combine(root, BankDir))
            ? [.. Directory.GetDirectories(Path.Combine(root, BankDir)).Select(d => Path.GetFileName(d)!).Where(SafeName).Order(StringComparer.Ordinal)]
            : [];

    public static Exam? FindExam(string root, string id)
    {
        if (!SafeName(id)) return null;
        var bank = Path.Combine(root, BankDir, id);
        if (!Directory.Exists(bank)) return null;
        var def = Json(Path.Combine(root, ExamsDir, id, ExamFile));
        var live = Json(Path.Combine(root, ExamsDir, id, LiveFile));

        var attempts = new List<Attempt>();
        foreach (var variantDir in Directory.GetDirectories(bank).Order(StringComparer.Ordinal))
        {
            // On an exam with axes a try sits under its own id, bank/<exam>/<try id>/stages/…, and its manifests say which configuration it ran as.
            if (Directory.Exists(Path.Combine(variantDir, StagesDir))) { attempts.AddRange(TryById(id, variantDir)); continue; }
            foreach (var taskDir in Directory.GetDirectories(variantDir).OrderBy(d => Path.GetFileName(d), Natural))
            {
                if (Directory.Exists(Path.Combine(taskDir, StagesDir)))
                {
                    attempts.AddRange(Stages(id, Path.GetFileName(variantDir), Path.GetFileName(taskDir), taskDir));
                    continue;
                }
                foreach (var attemptDir in Directory.GetDirectories(taskDir).OrderBy(d => Path.GetFileName(d), Natural))
                    if (File.Exists(Path.Combine(attemptDir, ManifestFile)))
                        attempts.Add(new Attempt(id, Path.GetFileName(variantDir), Path.GetFileName(taskDir), Path.GetFileName(attemptDir), attemptDir));
            }
        }

        return new Exam(id, Text(def?["description"]), Text(live?["variant"]), attempts) { Axes = def?["axes"] is JsonObject };
    }

    /// <summary>
    /// An attempt by the last two segments of its address. On an exam of stages they are the try and the stage's id; on
    /// an exam of tasks, the task and the attempt's number. The stages directory on disk says which.
    /// </summary>
    public static Attempt? FindAttempt(string root, string exam, string variant, string first, string second)
    {
        if (!SafeName(exam) || !SafeName(variant) || !SafeName(first) || !SafeName(second)) return null;
        var tryDir = Path.Combine(root, BankDir, exam, variant, first);
        if (Directory.Exists(Path.Combine(tryDir, StagesDir))) return Stages(exam, variant, first, tryDir).FirstOrDefault(s => s.Task == second);
        var dir = Path.Combine(tryDir, second);
        return File.Exists(Path.Combine(dir, ManifestFile)) ? new Attempt(exam, variant, first, second, dir) : null;
    }

    /// <summary>The stages of one try, in the order they ran, or none when it is not a try of stages.</summary>
    public static IReadOnlyList<Attempt> FindTry(string root, string exam, string variant, string n) =>
        SafeName(exam) && SafeName(variant) && SafeName(n) && Path.Combine(root, BankDir, exam, variant, n) is var tryDir && Directory.Exists(Path.Combine(tryDir, StagesDir))
            ? Stages(exam, variant, n, tryDir) : [];

    /// <summary>The stages of a try that sits under its own id (an exam with axes), in the order they ran; none when there is no such try.</summary>
    public static IReadOnlyList<Attempt> FindTryById(string root, string exam, string id) =>
        SafeName(exam) && SafeName(id) && Path.Combine(root, BankDir, exam, id) is var tryDir && Directory.Exists(Path.Combine(tryDir, StagesDir)) ? TryById(exam, tryDir) : [];

    // The configuration is the key proctor wrote in each stage's manifest; a try whose manifests cannot be read is listed under its id alone.
    static List<Attempt> TryById(string exam, string tryDir)
    {
        var id = Path.GetFileName(tryDir)!;
        var stages = Stages(exam, "", id, tryDir);
        var configuration = stages.Select(s => Text(s.Manifest?["variant"]?["id"])).FirstOrDefault(k => k is not null) ?? "";
        return [.. stages.Select(s => s with { Variant = configuration, ById = true })];
    }

    // A stage's directory is <place>-<id>; the place orders it and the id names it.
    static List<Attempt> Stages(string exam, string variant, string n, string tryDir) =>
        [.. Directory.GetDirectories(Path.Combine(tryDir, StagesDir))
            .Select(dir => (Dir: dir, Name: Path.GetFileName(dir)!))
            .Select(s => (s.Dir, Cut: s.Name.IndexOf('-'), s.Name))
            .Where(s => s.Cut > 0 && int.TryParse(s.Name[..s.Cut], out _) && File.Exists(Path.Combine(s.Dir, ManifestFile)))
            .OrderBy(s => int.Parse(s.Name[..s.Cut]))
            .Select(s => new Attempt(exam, variant, s.Name[(s.Cut + 1)..], n, s.Dir) { Place = int.Parse(s.Name[..s.Cut]) })];

    public static string Str(JsonNode? node) => node is JsonValue v ? v.ToString() : "";

    internal static string? Text(JsonNode? node) => Str(node) is { Length: > 0 } s ? s : null;

    internal static JsonObject? Json(string file)
    {
        try { return File.Exists(file) ? JsonNode.Parse(File.ReadAllText(file)) as JsonObject : null; }
        catch (JsonException) { return null; }
    }

    internal static string? Read(string file) => File.Exists(file) ? File.ReadAllText(file) : null;

    // A name from a URL becomes a path segment, so it may not climb or nest.
    static bool SafeName(string name) => name.Length > 0 && name != "." && name != ".." && name.IndexOfAny(['/', '\\', '\0']) < 0;

    static readonly IComparer<string?> Natural = Comparer<string?>.Create((a, b) =>
        int.TryParse(a, out var x) && int.TryParse(b, out var y) ? x.CompareTo(y) : StringComparer.Ordinal.Compare(a, b));

    internal static string ManifestPath(string dir) => Path.Combine(dir, ManifestFile);
    internal static string StatusPath(string dir) => Path.Combine(dir, StatusFile);
    internal static string TrajectoryPath(string dir) => Path.Combine(dir, TrajectoryFile);
    internal static string ChecksPath(string dir) => Path.Combine(dir, ChecksFile);
    internal static string FrictionPath(string dir) => Path.Combine(dir, FrictionFile);
    internal static string CorePath(string dir) => Path.Combine(dir, CoreFile);
}

/// <summary>An exam with a bank: its description and live variant from exams/&lt;exam&gt;/, and every attempt in bank/&lt;exam&gt;/.</summary>
public sealed record Exam(string Id, string? Description, string? Live, IReadOnlyList<Attempt> Attempts)
{
    /// <summary>Whether the exam declares axes (proctor's plans/axes.md): then what is under test is a configuration, and its page is ExamAxesPage.</summary>
    public bool Axes { get; init; }

    public string Path => $"/{Id}";

    /// <summary>The variants that have attempts, the live one first.</summary>
    public IReadOnlyList<string> Variants =>
        [.. Attempts.Select(a => a.Variant).Distinct().OrderBy(v => v == Live ? 0 : 1).ThenBy(v => v, StringComparer.Ordinal)];
}

/// <summary>
/// One attempt in the bank, and its files as proctor wrote them. On an exam of stages it is one stage of a try: Task is
/// then the stage's id, N the try, and Place where the stage comes in it.
/// </summary>
public sealed record Attempt(string Exam, string Variant, string Task, string N, string Dir)
{
    public int? Place { get; init; }
    public bool Staged => Place is not null;
    /// <summary>A stage of a try that sits under its own id: N is that id, and Variant the configuration's key.</summary>
    public bool ById { get; init; }
    public string Path => ById ? $"/{Exam}/try/{N}/{Task}" : Staged ? $"/{Exam}/{Variant}/{N}/{Task}" : $"/{Exam}/{Variant}/{Task}/{N}";

    /// <summary>Proctor's status file; an attempt without one has not started (proctor's own reading).</summary>
    public string Status => Bank.Read(Bank.StatusPath(Dir))?.Trim() is { Length: > 0 } s ? s : "pending";
    public JsonObject? Manifest => Bank.Json(Bank.ManifestPath(Dir));
    public JsonObject? Trajectory => Bank.Json(Bank.TrajectoryPath(Dir));
    public JsonObject? Checks => Bank.Json(Bank.ChecksPath(Dir));
    public JsonObject? Friction => Bank.Json(Bank.FrictionPath(Dir));
    public JsonObject? Core => Bank.Json(Bank.CorePath(Dir));
}
