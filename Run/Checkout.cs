namespace Proctor;

/// <summary>The work directory: a fixture materialised into it before the attempt, the diff collected after, and both again for a regrade.</summary>
static class Checkout
{
    /// <summary>A fresh checkout of the fixture in workDir, committed so the diff has a base. Null on success, else why not.</summary>
    public static string? Materialise(Fixture fixture, string workDir, IReadOnlyList<(string Dir, string? To)>? overlays = null) =>
        Base(fixture, workDir) ?? Overlay(workDir, overlays);

    /// <summary>
    /// The files a configuration's values carry, laid over the checkout and committed, so they are part of what every
    /// stage starts from: a diff shows only what the agent changed, and a fresh stage is restored to the base with them.
    /// </summary>
    static string? Overlay(string workDir, IReadOnlyList<(string Dir, string? To)>? overlays)
    {
        if (overlays is null or { Count: 0 }) return null;
        foreach (var (dir, to) in overlays)
            foreach (var (relative, full) in Tree.Files(dir, new HashSet<string> { ".git" }))
            {
                var dest = Path.Combine(workDir, to ?? "", relative);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(full, dest, overwrite: true);
            }
        return Git(workDir, "add", "-A")
            ?? Git(workDir, "-c", "user.name=proctor", "-c", "user.email=proctor@localhost", "commit", "-q", "--allow-empty", "-m", "values");
    }

    static string? Base(Fixture fixture, string workDir)
    {
        if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true);
        Directory.CreateDirectory(workDir);
        if (fixture.Source.Git is { } url)
        {
            if (Git(workDir, "clone", "-q", url, ".") is { } cloneError) return $"git clone {url}: {cloneError}";
            if (Git(workDir, "checkout", "-q", fixture.Source.Rev!) is { } checkoutError) return $"git checkout {fixture.Source.Rev}: {checkoutError}";
            return null;
        }
        foreach (var (relative, full) in fixture.SourceFiles())
        {
            var dest = Path.Combine(workDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(full, dest);
        }
        return Git(workDir, "init", "-q")
            ?? Git(workDir, "add", "-A")
            ?? Git(workDir, "-c", "user.name=proctor", "-c", "user.email=proctor@localhost", "commit", "-q", "--allow-empty", "-m", $"fixture {fixture.Id} {fixture.Hash}");
    }

    /// <summary>
    /// The checkout as the try began, for a stage that starts fresh: tracked files restored and new untracked ones
    /// removed. Ignored files stay (restored packages, build caches), or a build with no network would fail; a stale
    /// build output among them can survive. Null on success.
    /// </summary>
    public static string? Reset(string workDir) =>
        !Directory.Exists(Path.Combine(workDir, ".git")) ? "work directory is not a git checkout"
        : Git(workDir, "reset", "-q", "--hard", "HEAD") ?? Git(workDir, "clean", "-fdq");

    /// <summary>
    /// What a stage itself changed, given the diff so far before it and after it: the files whose part of the diff is
    /// new or different. On a chained exam the diff after a stage holds every earlier stage's changes too. A file an
    /// earlier stage changed and this one put back exactly is not seen.
    /// </summary>
    public static string Since(string? before, string after)
    {
        static List<string> Files(string diff) => System.Text.RegularExpressions.Regex.Split(diff, @"(?m)^(?=diff --git )").Where(s => s.Length > 0).ToList();
        if (string.IsNullOrEmpty(before)) return after;
        var earlier = Files(before).ToHashSet();
        return string.Concat(Files(after).Where(file => !earlier.Contains(file)));
    }

    /// <summary>Everything the attempt changed, new files included, as one unified diff in the attempt. Null on success.</summary>
    public static string? CollectDiff(string workDir, string attemptDir)
    {
        if (!Directory.Exists(Path.Combine(workDir, ".git"))) return "work directory is not a git checkout";
        if (Git(workDir, "add", "-A", "-N", ".") is { } addError) return addError;
        var result = Subprocess.Run("git", ["diff", "--no-color"], workDir, stdoutFile: Path.Combine(attemptDir, Layout.DiffFile));
        return result.Started && result.ExitCode == 0 ? null : $"git diff: {result.FirstStderrLine}";
    }

    /// <summary>For a regrade after the checkout is gone: the fixture again, with the attempt's diff applied. Null on success.</summary>
    public static string? Restore(Fixture fixture, string workDir, string attemptDir, IReadOnlyList<(string Dir, string? To)>? overlays = null)
    {
        if (Directory.Exists(workDir) && Directory.EnumerateFileSystemEntries(workDir).Any()) return null;
        if (Materialise(fixture, workDir, overlays) is { } error) return error;
        var diff = Path.Combine(attemptDir, Layout.DiffFile);
        if (!File.Exists(diff) || new FileInfo(diff).Length == 0) return null;
        return Git(workDir, "apply", "--allow-empty", diff) is { } applyError ? $"git apply {Layout.DiffFile}: {applyError}" : null;
    }

    static string? Git(string workDir, params string[] args)
    {
        var result = Subprocess.Run("git", args, workDir);
        if (!result.Started) return result.Stderr;
        return result.ExitCode == 0 ? null : $"git {args[0]} exited {result.ExitCode}: {result.FirstStderrLine}";
    }
}
