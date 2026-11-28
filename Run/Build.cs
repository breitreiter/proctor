using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Auspex;

// `proctor build` (plans/axes-build.md, slice 3b; plans/axes.md, decisions 15 and 16). A value's files and parameters
// are as flexible as the base's scripts make them, so what a value does is found by building it: the checkout, the
// values' files, the setup hooks, and nothing run. Each value is built with every other axis at its reference and held
// against the reference build; what differs is what the value changes, for a person or an agent to hold against the
// value's own sentence. A configuration of several values is held against its values built alone: where it is not what
// they add up to, some script reads more than one axis. Nothing here calls a model. A configuration that runs through
// Harbor has no checkout on the host: what is read is what its image build is handed (the compiled Dockerfile, the
// repository and the trees beside the services). The image is not built, so what a step of the Dockerfile does with an
// argument is not seen here: a check in the verifier is how that is held to account.

/// <summary>One path a value changed in the starting workspace: added, changed or removed, with its size before and after.</summary>
record PathChange(string Path, string Change, long? Before, long? After);

/// <summary>
/// What one value changes, built alone: who does the work when the value says, the parameters it sets (a secret by name
/// only), the paths that differ from the reference build, and their text where it is small. NotBuilt says why when it
/// could not be built, and then the rest is empty.
/// </summary>
record ValueChange(string Axis, string Value, string? Note, string Hash, string Base, string Reference, string? Tree,
    List<string> Who, Dictionary<string, string> Sets, List<PathChange> Paths, string Diff, int DiffLeftOut, string? NotBuilt, string? Compared = null, string? Why = null);

/// <summary>
/// A configuration of several values held against those values built alone. Differs: the paths where it is not what they
/// add up to. Unclear: the paths several of its values change alone in the same lines, where adding up has no meaning,
/// so nothing can be said either way.
/// </summary>
record CombinationResult(string Configuration, string Label, string? Name, string Hash, List<string> Differs, List<string> Unclear, string? NotBuilt);

/// <summary>One `proctor build`: the base it was built on, the reference, the paths two builds of the reference disagree on, each value's change and each combination's check.</summary>
record BuildResult(string Base, string Reference, List<string> Noise, List<ValueChange> Values, List<CombinationResult> Combinations);

static class Build
{
    /// <summary>A text file larger than this, and anything past this much diff in all, is listed by path and size only.</summary>
    const int MaxFile = 64 * 1024, MaxDiff = 40_000;

    /// <summary>Harbor is set when what was read is an image build's context and not a checkout on the host.</summary>
    sealed record Built(Dictionary<string, (long Size, string Hash)> Files, string? Tree, string WorkDir, bool Harbor = false);

    const string OfCheckout = "the starting workspace, after the setup hooks";
    const string OfContext = "what the image build is handed (Dockerfile, repo/, params/); the image is not built, so what its steps do with an argument is not shown";

    /// <summary>The configuration every value is built against: each axis at its live value, the reference it names, or its only value.</summary>
    public static Configuration Reference(Exam exam)
    {
        var axes = exam.Axes!;
        if (axes.Def.Keys.FirstOrDefault(a => axes.ReferenceOf(a) is null) is { } unheld)
            throw new AuspexException($"axis '{unheld}' has no live value and names no reference. A value is looked at with every other axis held at its reference, "
                + $"so give the axis \"reference\": \"<value>\" in exam.json ({string.Join(", ", axes.Def[unheld].Values!.Keys)})");
        return axes.Alone(new Dictionary<string, string>(), out _)!;
    }

    /// <summary>The reference of every axis, or null when some axis has none: then nothing can be built alone.</summary>
    public static Configuration? ReferenceOrNull(Exam exam) => exam.Axes!.Def.Keys.All(a => exam.Axes.ReferenceOf(a) is not null) ? Reference(exam) : null;

    /// <summary>
    /// What a value's change was measured against, as one hash: the reference configuration, its fixture, the declared
    /// parameters and the hook scripts. A combination's check holds while this and the configuration's own hash are today's.
    /// </summary>
    public static string BaseHash(Exam exam, Configuration reference)
    {
        var fixture = exam.StagesOf(reference.Key).FirstOrDefault() is { } stage ? exam.FixtureOf(stage) : null;
        var parts = new List<string> { reference.Hash, fixture?.Hash ?? "", JsonSerializer.Serialize(exam.Def.Params, Exam.JsonOptions), JsonSerializer.Serialize(fixture?.Def.Params, Exam.JsonOptions) };
        foreach (var script in Hooks(exam))
            parts.Add(script + " " + (File.Exists(Path.Combine(exam.Dir, script)) ? Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(exam.Dir, script)))) : ""));
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", parts))));
    }

    static IEnumerable<string> Hooks(Exam exam) =>
        new[] { exam.Def.Hooks?.Configuration?.Setup, exam.Def.Hooks?.Configuration?.Teardown, exam.Def.Hooks?.Try?.Setup, exam.Def.Hooks?.Try?.Teardown }.OfType<string>();

    /// <summary>The axes on which a configuration is not at the reference.</summary>
    public static List<(string Axis, string Value)> Off(Configuration c, Configuration reference) => c.Values.Where(v => reference[v.Axis] != v.Value).ToList();

    /// <summary>
    /// Build the reference twice, then each value alone (or only the one named), then each configuration the exam carries
    /// that is more than one value off the reference. What each left is under .proctor/builds/&lt;exam&gt;/.
    /// </summary>
    public static BuildResult Run(string root, Exam exam, AuspexConfig config, string? nbOverride, string? runnerOverride, (string Axis, string Value)? only, TextWriter log)
    {
        var axes = exam.Axes!;
        var reference = Reference(exam);
        var @base = BaseHash(exam, reference);
        var dir = Path.Combine(root, Layout.BuildsDir, exam.Id);
        var scratch = Path.Combine(dir, "work");
        if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
        Directory.CreateDirectory(scratch);
        // The hooks are told where nb is, as in a run; an exam without hooks needs none to be built.
        var nb = Hooks(exam).Any() ? Runner.ResolveNb(root, exam, config, nbOverride, runnerOverride) : null;

        var (first, whyNot) = One(exam, nb, reference, "reference", scratch);
        if (first is null) throw new AuspexException($"the reference ({reference.Label}) could not be built: {whyNot}");
        // Built again: a path two builds of the same thing disagree on is the build's own, and would read as every value's change.
        var (again, _) = One(exam, nb, reference, "reference-again", scratch);
        var noise = again is null ? [] : Differing(first.Files, again.Files).Order(StringComparer.Ordinal).ToList();
        log.WriteLine($"reference: {reference.Label}{(noise.Count > 0 ? $"  ({Count(noise.Count, "path")} not the same in two builds of it, left out of every change: {string.Join(", ", noise.Take(5))}{(noise.Count > 5 ? ", …" : "")})" : "")}");

        var alone = new Dictionary<(string, string), Built>();
        var changes = new List<ValueChange>();
        var wanted = only is { } one ? [one] : axes.Def.SelectMany(a => a.Value.Values!.Keys.Where(v => v != axes.ReferenceOf(a.Key)).Select(v => (Axis: a.Key, Value: v))).ToList();
        foreach (var (axis, value) in wanted)
        {
            var c = axes.Alone(new Dictionary<string, string> { [axis] = value }, out _)!;
            var def = axes.Value(axis, value);
            ValueChange NotBuilt(string why) => new(axis, value, def.Note, axes.ValueHashes[(axis, value)], @base, reference.Key, null, [], [], [], "", 0, why, Why: def.Why);
            if (c.Excluded) { changes.Add(NotBuilt("excluded with the reference values, so it cannot be built alone")); continue; }
            var (built, why) = One(exam, nb, c, $"{axis}={value}", scratch);
            if (built is null) { changes.Add(NotBuilt(why!)); continue; }
            alone[(axis, value)] = built;
            // A value that moves the work between the host and a container changes where everything is: the two are not compared path by path.
            var comparable = built.Harbor == first.Harbor;
            var paths = (comparable ? Differing(first.Files, built.Files).Except(noise) : []).Order(StringComparer.Ordinal).Select(p =>
                new PathChange(p, !first.Files.ContainsKey(p) ? "added" : !built.Files.ContainsKey(p) ? "removed" : "changed",
                    first.Files.TryGetValue(p, out var before) ? before.Size : null, built.Files.TryGetValue(p, out var after) ? after.Size : null)).ToList();
            var (diff, leftOut) = DiffOf(scratch, first, built, paths);
            var fixture = exam.FixtureOf(exam.StagesOf(c.Key)[0]);
            changes.Add(new ValueChange(axis, value, def.Note, axes.ValueHashes[(axis, value)], @base, reference.Key, built.Tree,
                def.Own().Except(axes.Value(axis, reference[axis]!).Own()).Where(o => o.Value is not null).Select(o => $"{o.Key} {o.Value}").ToList(),
                (def.Set ?? []).ToDictionary(s => s.Key, s => Shown(exam.Def.Params?.GetValueOrDefault(s.Key) ?? fixture?.Def.Params?.GetValueOrDefault(s.Key), s.Value)),
                paths, diff, leftOut, null, comparable ? (built.Harbor ? OfContext : OfCheckout) : "nothing: this value moves the work between the host and a container, so the two starting points are not compared path by path", def.Why));
        }

        // A configuration of several values, against its values alone. Where no script reads two axes, a path is the
        // reference's unless one of the values changed it, and then it is that value's.
        var combinations = new List<CombinationResult>();
        foreach (var c in only is null ? axes.Configurations.Where(c => Off(c, reference).Count > 1) : [])
        {
            var off = Off(c, reference);
            var missing = off.Where(v => !alone.ContainsKey(v)).Select(v => $"{v.Axis}={v.Value}").ToList();
            var (built, why) = missing.Count > 0 ? (null, $"{string.Join(" and ", missing)} could not be built alone") : One(exam, nb, c, "configuration-" + c.Hash[7..15], scratch);
            if (built is null) { combinations.Add(new CombinationResult(c.Key, c.Label, c.Name, c.Hash, [], [], why)); continue; }
            var (differs, unclear) = (new List<string>(), new List<string>());
            foreach (var path in Differing(first.Files, built.Files).Concat(off.SelectMany(v => Differing(first.Files, alone[v].Files))).Distinct().Except(noise).Order(StringComparer.Ordinal))
            {
                var by = off.Where(v => Same(first.Files, alone[v].Files, path) is false).ToList();
                if (by.Count > 1)
                {
                    // Several values each change this path alone, as when one prompt file is written from several parameters. That is no
                    // fault while their changes are to different lines: merged over the reference they must give what was built.
                    var names = string.Join(" and ", by.Select(v => $"{v.Axis}={v.Value}"));
                    var merged = Merged(scratch, first, by.Select(v => alone[v]).ToList(), path, out var overlap);
                    if (overlap) unclear.Add($"{path} ({names} each change it alone, in the same or neighbouring lines, or it is not small text)");
                    else if (merged != File.ReadAllText(Path.Combine(built.WorkDir, path))) differs.Add($"{path} (not what the changes of {names} alone come to)");
                }
                else if (Same(by.Count == 1 ? alone[by[0]].Files : first.Files, built.Files, path) is false)
                    differs.Add(by.Count == 1 ? $"{path} (not as {by[0].Axis}={by[0].Value} alone leaves it)" : $"{path} (no value alone changes it)");
            }
            combinations.Add(new CombinationResult(c.Key, c.Label, c.Name, c.Hash, differs, unclear, null));
        }

        foreach (var change in changes) Runner.WriteJson(Path.Combine(dir, $"{change.Axis}={change.Value}.json"), change);
        foreach (var combination in combinations) Runner.WriteJson(Path.Combine(dir, $"configuration.{combination.Hash[7..15]}.json"), combination);
        Directory.Delete(scratch, recursive: true);
        return new BuildResult(@base, reference.Key, noise, changes, combinations);
    }

    /// <summary>One configuration's starting workspace: the checkout with its values' files, then the setup hooks, committed and read; the teardown hooks after. Null with why when it cannot be built.</summary>
    static (Built? Built, string? Why) One(Exam exam, ResolvedNb? nb, Configuration c, string label, string scratch)
    {
        var axes = exam.Axes!;
        var stages = exam.StagesOf(c.Key);
        if (stages.Count == 0) return (null, "no stage applies in it");
        var fixture = exam.FixtureOf(stages[0]);
        if (axes.AssistantOf(c) is { IsAgent: true } agent)
        {
            // Through Harbor: the task as `run` would compile it, with no route and a fixed name for nb's layer, since neither is the value's.
            if (fixture?.Def.Image is null) return (null, "runs through Harbor, and its fixture declares no image");
            var task = Path.Combine(scratch, label);
            HarborCompile.Job(task, exam, agent, new AgentRoute(null, null), nbImage: "proctor-nb");
            var handed = Path.Combine(task, "environment");
            return (new Built(Directory.EnumerateFiles(handed, "*", SearchOption.AllDirectories)
                .ToDictionary(f => Path.GetRelativePath(handed, f), f => (new FileInfo(f).Length, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(f))))), null, handed, Harbor: true), null);
        }
        var (work, attemptDir) = (Path.Combine(scratch, label), Path.Combine(scratch, label + ".build"));
        Directory.CreateDirectory(work);
        Directory.CreateDirectory(attemptDir);
        var context = new AttemptContext(exam.Dir, attemptDir, work, exam.Id, c.Key, stages[0], 0)
        {
            Axis = c.Values.ToDictionary(v => v.Axis, v => v.Value), Params = axes.Params(c, exam.Def, fixture, exam.Dir), Overlays = axes.Overlays(c, exam.Dir),
            Expect = exam.ExpectFor(stages[0]), Fixture = fixture, NbRunner = nb?.Runner?.Script ?? "", NbPath = nb?.Path ?? "", NbConfig = nb?.Config, Mounts = nb?.Mounts,
        };
        if (fixture is not null && Checkout.Materialise(fixture, work, context.Overlays) is { } checkoutError) return (null, $"fixture checkout failed: {checkoutError}");

        var hookEnv = new Dictionary<string, string> { ["PROCTOR_EXAM"] = exam.Id, ["PROCTOR_CONFIGURATION"] = c.Key, ["PROCTOR_EXAM_DIR"] = exam.Dir, ["PROCTOR_RUNNER"] = nb?.Runner?.Script ?? "" };
        foreach (var (axis, value) in c.Values) hookEnv["PROCTOR_AXIS_" + axis.ToUpperInvariant().Replace('-', '_')] = value;
        string Log(string name) => Path.Combine(attemptDir, name + ".log");
        var failure = Runner.RunHook(exam, exam.Def.Hooks?.Configuration?.Setup, hookEnv, Log("configuration.setup")) is { } setupFailed ? $"configuration setup hook failed: {setupFailed}" : null;
        var env = context.Environment();
        var trySetupRan = failure is null;
        if (failure is null && Runner.RunHook(exam, exam.Def.Hooks?.Try?.Setup, env, Log("try.setup")) is { } setupError) failure = $"try setup hook failed: {setupError}";

        string? tree = null;
        var files = new Dictionary<string, (long, string)>();
        if (failure is null && fixture is not null)
        {
            failure = Checkout.CommitSetup(work, Path.Combine(attemptDir, Layout.SetupPatchFile), out tree);
            var listed = Subprocess.Run("git", ["ls-files", "-z"], work);
            if (failure is null && listed is not { Started: true, ExitCode: 0 }) failure = $"git ls-files: {listed.FirstStderrLine}";
            if (failure is null)
                foreach (var path in listed.Stdout.Split('\0', StringSplitOptions.RemoveEmptyEntries))
                    if (new FileInfo(Path.Combine(work, path)) is { Exists: true } file)
                        files[path] = (file.Length, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file.FullName))));
        }
        // A tree parameter is files the program or a script is handed, outside the checkout: they are part of what the value changes,
        // so they are read too, under params/<NAME>/ as a task through Harbor has them.
        if (failure is null)
            foreach (var (name, declared) in (exam.Def.Params ?? []).Concat(fixture?.Def.Params ?? []).Where(p => p.Value.Tree == true))
                if (context.Params.GetValueOrDefault(name) is { Length: > 0 } given && Directory.Exists(given))
                    foreach (var (relative, full) in Tree.Files(given, new HashSet<string> { ".git" }))
                    {
                        var path = Path.Combine("params", name, relative);
                        var copy = Path.Combine(work, path);
                        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                        File.Copy(full, copy, overwrite: true);
                        files[path] = (new FileInfo(copy).Length, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(copy))));
                    }
        // Teardown runs whenever setup ran, as in a try; what it does is after the starting state and in no change.
        if (trySetupRan) Runner.RunHook(exam, exam.Def.Hooks?.Try?.Teardown, env, Log("try.teardown"));
        Runner.RunHook(exam, exam.Def.Hooks?.Configuration?.Teardown, hookEnv, Log("configuration.teardown"));
        return failure is null ? (new Built(files, tree, work), null) : (null, failure);
    }

    /// <summary>
    /// A path several values each change alone, as their changes merged over the reference's copy, line by line (git
    /// merge-file). Overlap is set when they touch the same lines, or the path is not small text in every build, or is
    /// missing from one: then nothing can be said.
    /// </summary>
    static string? Merged(string scratch, Built reference, List<Built> alone, string path, out bool overlap)
    {
        overlap = true;
        string Copy(Built b) => Path.Combine(b.WorkDir, path);
        if (alone.Append(reference).Any(b => !b.Files.TryGetValue(path, out var f) || f.Size > MaxFile || File.ReadAllBytes(Copy(b)).Contains((byte)0))) return null;
        var current = Path.Combine(scratch, "merge.tmp");
        File.Copy(Copy(alone[0]), current, overwrite: true);
        foreach (var other in alone.Skip(1))
        {
            var result = Subprocess.Run("git", ["merge-file", "-p", current, Copy(reference), Copy(other)], scratch);
            if (!result.Started || result.ExitCode != 0) return null;
            File.WriteAllText(current, result.Stdout);
        }
        overlap = false;
        return File.ReadAllText(current);
    }

    static IEnumerable<string> Differing(Dictionary<string, (long Size, string Hash)> a, Dictionary<string, (long Size, string Hash)> b) =>
        a.Keys.Union(b.Keys).Where(p => Same(a, b, p) is false);

    static bool Same(Dictionary<string, (long Size, string Hash)> a, Dictionary<string, (long Size, string Hash)> b, string path) =>
        a.TryGetValue(path, out var x) == b.TryGetValue(path, out var y) && x.Hash == y.Hash;

    /// <summary>A parameter as a change shows it: a secret by the name of its variable, never its value; a tree as declared; text as it is.</summary>
    static string Shown(ParamDef? declared, JsonNode? given) =>
        given is null ? "" : declared?.Secret == true ? $"(secret, from ${given})" : given is JsonValue ? given.ToString() : given.ToJsonString();

    /// <summary>The changed paths as one unified diff, where each is small text; the count of paths left to the list.</summary>
    static (string Diff, int LeftOut) DiffOf(string scratch, Built reference, Built built, List<PathChange> paths)
    {
        var diff = new StringBuilder();
        var leftOut = 0;
        foreach (var path in paths)
        {
            var (before, after) = (Path.Combine(reference.WorkDir, path.Path), Path.Combine(built.WorkDir, path.Path));
            if (diff.Length > MaxDiff || !Text(path.Before, before) || !Text(path.After, after)) { leftOut++; continue; }
            var result = Subprocess.Run("git", ["diff", "--no-index", "--no-color", "--", path.Before is null ? "/dev/null" : Path.GetRelativePath(scratch, before), path.After is null ? "/dev/null" : Path.GetRelativePath(scratch, after)], scratch);
            if (result.Started && result.ExitCode is 0 or 1) diff.Append(result.Stdout);
            else leftOut++;
        }
        return (diff.ToString(), leftOut);

        static bool Text(long? size, string file)
        {
            if (size is null) return true;
            if (size > MaxFile) return false;
            using var stream = File.OpenRead(file);
            var head = new byte[Math.Min(8000, (int)stream.Length)];
            stream.ReadExactly(head);
            return !head.Contains((byte)0);
        }
    }

    /// <summary>A value's change as lines a person or an agent reads: the value's own sentence first, since that is what the change is held against.</summary>
    public static IEnumerable<string> Describe(ValueChange change, bool withDiff)
    {
        yield return $"{change.Axis}={change.Value}";
        yield return $"  says: {change.Note ?? "(no note)"}";
        if (change.Why is not null) yield return $"  there because: {change.Why}";
        if (change.NotBuilt is not null) { yield return $"  not built: {change.NotBuilt}"; yield break; }
        if (change.Compared is not null && change.Compared != OfCheckout) yield return $"  compared: {change.Compared}";
        foreach (var who in change.Who) yield return $"  who does the work: {who}";
        foreach (var (name, value) in change.Sets) yield return $"  sets {name} = {value}";
        foreach (var path in change.Paths)
            yield return $"  {path.Change,-7}  {path.Path}  ({(path.Before is null ? "" : Bytes(path.Before.Value))}{(path.Before is not null && path.After is not null ? " to " : "")}{(path.After is null ? "" : Bytes(path.After.Value))})";
        if (change.Who.Count == 0 && change.Sets.Count == 0 && change.Paths.Count == 0) yield return "  changes nothing in the starting workspace, and sets nothing";
        else if (change.Paths.Count == 0 && change.Sets.Count > 0 && change.Compared == OfCheckout) yield return "  no path in the starting workspace differs: what it sets is read later, by the program or a check, or by nothing";
        if (!withDiff) yield break;
        if (change.Diff.Length > 0)
            foreach (var line in change.Diff.TrimEnd('\n').Split('\n')) yield return "    " + line;
        if (change.DiffLeftOut > 0) yield return $"  {Count(change.DiffLeftOut, "path")} not shown as text (large or not text): see the list above";
    }

    static string Bytes(long n) => n < 1024 ? $"{n} B" : n < 1024 * 1024 ? $"{n / 1024.0:0.#} KB" : $"{n / (1024.0 * 1024):0.#} MB";
    static string Count(int n, string one) => $"{n} {one}{(n == 1 ? "" : "s")}";
}
