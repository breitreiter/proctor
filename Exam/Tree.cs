using System.Security.Cryptography;
using System.Text;

namespace Proctor;

/// <summary>A directory as a definition: its files minus excluded directory names, and a content hash over them.</summary>
static class Tree
{
    /// <summary>Every file under dir as (relative path, full path), sorted; directories named in excluded are skipped at any depth.</summary>
    public static IEnumerable<(string Relative, string Full)> Files(string dir, IReadOnlySet<string> excluded)
    {
        var files = new List<(string, string)>();
        void Walk(string d)
        {
            foreach (var sub in Directory.GetDirectories(d))
                if (!excluded.Contains(Path.GetFileName(sub))) Walk(sub);
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
