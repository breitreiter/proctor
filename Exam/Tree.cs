using System.Security.Cryptography;
using System.Text;

namespace Auspex;

/// <summary>A directory as a definition: its files minus excluded directory names, and a content hash over them.</summary>
static class Tree
{
    /// <summary>
    /// Directories a tool leaves behind when a test or a script is run where it lies, skipped in every tree at any depth:
    /// they are no part of what anyone wrote, they differ from machine to machine, and git ignores them, so a tree that
    /// held one was another fixture and other checks on the machine that had run its shakedown.
    /// </summary>
    public static readonly IReadOnlySet<string> Litter = new HashSet<string> { "__pycache__", ".pytest_cache", ".mypy_cache", ".ruff_cache" };

    /// <summary>Every file under dir as (relative path, full path), sorted; directories named in excluded, and <see cref="Litter"/>, are skipped at any depth.</summary>
    public static IEnumerable<(string Relative, string Full)> Files(string dir, IReadOnlySet<string> excluded)
    {
        var files = new List<(string, string)>();
        void Walk(string d)
        {
            foreach (var sub in Directory.GetDirectories(d))
                if (!excluded.Contains(Path.GetFileName(sub)) && !Litter.Contains(Path.GetFileName(sub))) Walk(sub);
            foreach (var file in Directory.GetFiles(d))
                files.Add((Path.GetRelativePath(dir, file).Replace('\\', '/'), file));
        }
        Walk(dir);
        return files.OrderBy(f => f.Item1, StringComparer.Ordinal);
    }

    public static string Hash(string dir, IReadOnlySet<string> excluded)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (relative, full) in Files(dir, excluded))
        {
            sha.AppendData(Encoding.UTF8.GetBytes(relative + "\0"));
            sha.AppendData(File.ReadAllBytes(full));
            sha.AppendData("\0"u8);
        }
        return "sha256:" + Convert.ToHexStringLower(sha.GetHashAndReset());
    }
}
