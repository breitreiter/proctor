namespace Auspex;

/// <summary>
/// A value's files from a git revision: the repository cloned once per revision under .proctor/files/, and, when the
/// value names paths, only those files and directories copied out into a directory of their own, named by the source's
/// identity so two subsets of one revision sit apart. Fetched when a configuration that carries them is built or run,
/// never by `list`: a value's identity is its revision and paths, which need no clone to know.
/// </summary>
static class GitFiles
{
    /// <summary>The directory holding what the source names, cloning and copying on first use. <paramref name="what"/> says whose files they are, for a failure.</summary>
    public static string Fetch(string root, FilesSource source, string what)
    {
        var clone = Path.Combine(root, Layout.GitFilesDir, source.Rev!);
        if (!Directory.Exists(Path.Combine(clone, ".git")))
        {
            if (Directory.Exists(clone)) Directory.Delete(clone, recursive: true);
            Directory.CreateDirectory(clone);
            foreach (var args in new[] { new[] { "clone", "-q", source.Git!, "." }, new[] { "checkout", "-q", source.Rev! } })
            {
                var result = Subprocess.Run("git", args, clone);
                if (result is { Started: true, ExitCode: 0 }) continue;
                Directory.Delete(clone, recursive: true);   // a half-made clone would pass for the revision next time
                throw new AuspexException($"{what}: git {args[0]} {source.Git} failed: {(result.Started ? result.FirstStderrLine : result.Stderr)}");
            }
        }
        if (source.Paths is not { } paths) return clone;

        var identity = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source.GitIdentity())));
        var dir = Path.Combine(root, Layout.GitFilesDir, $"{source.Rev}-{identity[..12]}");
        if (Directory.Exists(dir)) return dir;
        var staging = dir + ".partial";
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);
        foreach (var path in paths)
        {
            var from = Path.GetFullPath(Path.Combine(clone, path));
            var to = Path.Combine(staging, path);
            if (File.Exists(from)) { Directory.CreateDirectory(Path.GetDirectoryName(to)!); File.Copy(from, to, overwrite: true); }
            else if (Directory.Exists(from) && Path.GetFileName(from.TrimEnd(Path.DirectorySeparatorChar)) != ".git")
                foreach (var (relative, full) in Tree.Files(from, new HashSet<string> { ".git" }))
                {
                    var target = Path.Combine(to, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(full, target, overwrite: true);
                }
            else
            {
                Directory.Delete(staging, recursive: true);
                // A path the revision does not have is an error, not an empty tree.
                throw new AuspexException($"{what}: {path} is not in {source.Git} at {source.Rev}");
            }
        }
        Directory.Move(staging, dir);
        return dir;
    }
}
