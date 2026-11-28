using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Auspex.View.View.Tests;

/// <summary>
/// A proctor root with an exam of axes and a real bank, made once for the tests that share it: proctor's own scripted
/// exam with known answers (Auspex.Tests/exams/axes-answers), one try each of four configurations, run and graded by
/// the proctor built beside these tests. No model: nb's Mock provider, as proctor's own tests use it.
/// </summary>
public sealed class AxesBank : IDisposable
{
    public const string Exam = "axes-answers";
    readonly string _temp = Directory.CreateTempSubdirectory("auspex-axes-").FullName;

    public string Root { get; }
    /// <summary>The id of each configuration's one try, by the configuration's name.</summary>
    public Dictionary<string, string> Tries { get; } = [];

    public AxesBank()
    {
        Root = Path.Combine(_temp, "proctor");
        var repo = RepoRoot();
        Copy(Path.Combine(repo, "Auspex.Tests", "exams", Exam), Path.Combine(Root, "exams", Exam));
        Copy(Path.Combine(repo, "fixtures", "note"), Path.Combine(Root, "fixtures", "note"));
        File.Copy(Path.Combine(repo, "exams", "nb.example.json"), Path.Combine(Root, "exams", "nb.json"));
        var nb = Environment.GetEnvironmentVariable("NB_PATH") ?? Path.GetFullPath(Path.Combine(repo, "..", "nb", "bin", "Debug", "net10.0", "nb"));
        File.WriteAllText(Path.Combine(Root, "exams", "auspex.json"), new JsonObject { ["nb"] = new JsonObject { ["path"] = nb, ["config"] = "nb.json" } }.ToJsonString());
        var examFile = Path.Combine(Root, "exams", Exam, "exam.json");
        var exam = JsonNode.Parse(File.ReadAllText(examFile))!;
        exam["tries"] = 1;
        File.WriteAllText(examFile, exam.ToJsonString());

        foreach (var name in new[] { "shipped", "shipped-b", "rewritten", "rewritten-b" })
        {
            var before = Made();
            Proctor("run", Exam, "--configuration", name);
            Tries[name] = Made().Except(before).Single();
        }
        Proctor("grade", Exam);
    }

    HashSet<string> Made() => Directory.Exists(Path.Combine(Root, "bank", Exam)) ? [.. Directory.GetDirectories(Path.Combine(Root, "bank", Exam)).Select(d => Path.GetFileName(d)!)] : [];

    /// <summary>Run the proctor built in this repository over the root, and give what it printed.</summary>
    public string Proctor(params string[] args)
    {
        var start = new ProcessStartInfo("dotnet", [Path.Combine(RepoRoot(), "bin", "Debug", "net10.0", "auspex.dll"), "--root", Root, .. args]) { RedirectStandardOutput = true, RedirectStandardError = true };
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"proctor {string.Join(' ', args)} exited {process.ExitCode}: {error.Result}");
        return output.Result;
    }

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    static void Copy(string from, string to)
    {
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(target, File.GetUnixFileMode(file));
        }
    }

    /// <summary>The repository's root: the directory holding Auspex.csproj, above the viewer's.</summary>
    static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "Auspex.csproj"))) dir = Path.GetDirectoryName(dir) ?? throw new InvalidOperationException("Auspex.csproj not found above the tests");
        return dir;
    }
}
