using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Proctor;

// Predictions (plans/axes-build.md, slice 6; the word is the user's, 2026-11-25). A prediction is one statement, written
// before the tries are made: "if this configuration is run, this is what will change." It names one full configuration,
// what it is compared with, and the changes expected: each a check or a metric, and a direction. After the tries,
// proctor says how each change came out, a miss as plainly as a hit. Nothing is refused for having no prediction, and
// a prediction gates nothing. exams/<exam>/predictions/<name>.json, committed with the exam and in no hash.
// There is no word for "a configuration that might replace live": that construct is not needed yet (the user).

/// <summary>One change a prediction expects: a check (it will hold in more or fewer stages of a try) or a metric (there will be more or less of it), and optionally why.</summary>
record ChangeDef(string? Check = null, string? Metric = null, string? Direction = null, string? Because = null);

/// <summary>
/// A prediction as its file holds it. Configuration and Against name configurations the exam names; without Against it
/// is what is live for the same user (the live value on every axis that has one, the rest as the prediction has them).
/// Changes are what it expects. Written is its author's and required: when the prediction was made, a date or a time in UTC.
/// Standard is proctor's pin, set once: each stage's standard when proctor first saw the prediction.
/// </summary>
record PredictionDef(string? Configuration = null, string? Against = null, List<ChangeDef>? Changes = null, string? Note = null,
    string? Written = null, Dictionary<string, string>? Standard = null);

sealed record Prediction(string Name, PredictionDef Def, Configuration Configuration, Configuration Against, string File);

static class Predictions
{
    public const string More = "more", Less = "less";
    /// <summary>The numbers a try gives that a prediction may be about, beside a check: each with what "more" of it means in words.</summary>
    public static readonly Dictionary<string, string> Metrics = new()
    {
        ["held"] = "correctness checks held", ["flagged_steps"] = "flagged steps", ["tool_calls"] = "tool calls", ["tokens"] = "tokens read", ["duration"] = "time taken",
    };

    public static string DirOf(Exam exam) => Path.Combine(exam.Dir, Layout.PredictionsDir);

    /// <summary>Every prediction of an exam with axes, validated: each problem names its file and field and says what to change.</summary>
    public static List<Prediction> Load(string root, Exam exam, List<Problem> problems)
    {
        var dir = DirOf(exam);
        var predictions = new List<Prediction>();
        if (!Directory.Exists(dir)) return predictions;
        foreach (var file in Directory.GetFiles(dir, "*.json").Order(StringComparer.Ordinal))
        {
            var rel = Path.GetRelativePath(root, file);
            void Add(string field, string message) => problems.Add(new Problem(rel, field, message));
            var before = problems.Count;
            var name = Path.GetFileNameWithoutExtension(file);
            var axes = exam.Axes;
            if (!Regex.IsMatch(name, "^[a-z0-9][a-z0-9._-]*$")) Add("", "a prediction is named in lowercase letters, digits, dots, hyphens and underscores");
            PredictionDef? def;
            try { def = JsonSerializer.Deserialize<PredictionDef>(File.ReadAllText(file), Exam.JsonOptions); }
            catch (JsonException e) { Add("", $"not JSON a prediction can be read from: {e.Message}"); continue; }
            if (def is null) { Add("", "a prediction is an object: {configuration, changes}"); continue; }

            // One full configuration, by the name the exam gives it: never a value with the other axes however they fall.
            Configuration? Named(string field, string? given)
            {
                var found = given is null ? null : axes.Named.FirstOrDefault(c => c.Name == given);
                if (given is not null && found is null) Add(field, $"'{given}' is not a configuration this exam names ({string.Join(", ", axes.Named.Select(c => c.Name!).DefaultIfEmpty("none"))}); a prediction is about one full configuration, named under \"configurations\" in exam.json");
                return found;
            }
            if (def.Configuration is null) Add("configuration", "required: the configuration this prediction is about, by the name the exam gives it");
            var configuration = Named("configuration", def.Configuration);
            var against = def.Against is null
                ? configuration is null ? null : axes.Make(configuration.Values.ToDictionary(v => v.Axis, v => axes.Def[v.Axis].Live ?? v.Value))
                : Named("against", def.Against);
            if (configuration is not null && against is not null && configuration.Key == against.Key)
                Add(def.Against is null ? "configuration" : "against", def.Against is null
                    ? "has the live value on every axis that has one, so there is nothing live to compare it with: say \"against\", the configuration it is compared with"
                    : "the same configuration the prediction is about");

            // When it was made is the author's to say, and required: it is what "before the tries" is measured against.
            if (def.Written is null) Add("written", "required: when this prediction was made, as a date (2026-11-24) or a time in UTC (2026-11-24T21:05Z)");
            else if (WrittenAt(def.Written) is null) Add("written", $"'{def.Written}' is not a date (2026-11-24) or a time in UTC (2026-11-24T21:05Z)");

            if (def.Changes is null or { Count: 0 }) Add("changes", "at least one is required: {\"check\": …, \"direction\": \"more\"|\"less\"} or {\"metric\": …, \"direction\": …}");
            foreach (var (p, i) in (def.Changes ?? []).Select((p, i) => (p, i)))
            {
                var at = $"changes[{i}]";
                if (p is null) { Add(at, "a change is an object: a check or a metric, and a direction"); continue; }
                if ((p.Check is null) == (p.Metric is null)) Add(at, "names one thing: a check, or a metric");
                if (p.Direction is not (More or Less)) Add($"{at}.direction", $"\"{More}\" or \"{Less}\": which way it will move against {(def.Against ?? "what is live")}; a size is not asked for");
                if (p.Metric is not null && !Metrics.ContainsKey(p.Metric)) Add($"{at}.metric", $"'{p.Metric}' is not a number a try gives; known: {string.Join(", ", Metrics.Keys)}");
                if (p.Check is not null && configuration is not null && !exam.StagesOf(configuration.Key).Any(s => exam.ChecksFor(s).TryGetValue(p.Check, out var check) && Standard.Applies(exam, check, configuration.Key)))
                    Add($"{at}.check", $"'{p.Check}' is not a check any stage carries in {configuration.Name}");
                if (p.Because is not null && string.IsNullOrWhiteSpace(p.Because)) Add($"{at}.because", "a sentence, not an empty string");
            }
            if (problems.Count == before) predictions.Add(new Prediction(name, def, configuration!, against!, file));
        }
        return predictions;
    }

    /// <summary>
    /// Pin a prediction: the standard it was made under, each stage's. Done once; a prediction already pinned is left
    /// as it is, since the pin is what a moved standard is measured against. When it was made is its author's (written),
    /// never set here. The file is rewritten with the field added and everything else as its author wrote it. False when it was pinned already.
    /// </summary>
    public static bool Stamp(Exam exam, Prediction prediction)
    {
        if (prediction.Def.Standard is not null) return false;
        var node = JsonNode.Parse(File.ReadAllText(prediction.File), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsObject();
        node["standard"] = new JsonObject(exam.StagesOf(prediction.Configuration.Key).Select(s => KeyValuePair.Create(s.Id!, (JsonNode?)Standard.Hash(exam, s))));
        File.WriteAllText(prediction.File, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
        return true;
    }

    /// <summary>When a prediction says it was made, or null when that is not a date or a time: a date alone is the end of that day, so a try made the same day counts as before it.</summary>
    public static DateTime? WrittenAt(string written)
    {
        try { return Answers.ParseAsOf(written); }
        catch (ProctorException) { return null; }
    }

    /// <summary>One change as a person would say it: what, which way, against what.</summary>
    public static string Said(ChangeDef p, Prediction c) =>
        (p.Check is not null ? $"{p.Check} to hold in {(p.Direction == More ? "more" : "fewer")} stages"
            : $"{(p.Direction == More ? "more" : p.Metric == "duration" ? "less" : p.Metric is "held" or "tool_calls" or "tokens" or "flagged_steps" ? "fewer" : "less")} {Metrics[p.Metric!]}")
        + $" than {c.Against.Name ?? c.Against.Label}";
}
