namespace Proctor;

/// <summary>The work directory: a fixture materialised into it before the run, the diff collected after, and both again for a regrade.</summary>
static class Checkout
{
    /// <summary>A fresh checkout of the fixture in workDir, committed so the diff has a base. Null on success, else why not.</summary>
    public static string? Materialise(Fixture fixture, string workDir)
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

    /// <summary>Everything the run changed, new files included, as one unified diff in the cell. Null on success.</summary>
    public static string? CollectDiff(string workDir, string cellDir)
    {
        if (!Directory.Exists(Path.Combine(workDir, ".git"))) return "work directory is not a git checkout";
        if (Git(workDir, "add", "-A", "-N", ".") is { } addError) return addError;
        var result = Subprocess.Run("git", ["diff", "--no-color"], workDir, stdoutFile: Path.Combine(cellDir, Layout.DiffFile));
        return result.Started && result.ExitCode == 0 ? null : $"git diff: {result.FirstStderrLine}";
    }

    /// <summary>For a regrade after the checkout is gone: the fixture again, with the cell's diff applied. Null on success.</summary>
    public static string? Restore(Fixture fixture, string workDir, string cellDir)
    {
        if (Directory.Exists(workDir) && Directory.EnumerateFileSystemEntries(workDir).Any()) return null;
        if (Materialise(fixture, workDir) is { } error) return error;
        var diff = Path.Combine(cellDir, Layout.DiffFile);
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
