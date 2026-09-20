using System.Diagnostics;

namespace Proctor;

record SubprocessResult(int ExitCode, string Stdout, string Stderr, bool Started)
{
    public string FirstStderrLine => Stderr.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
}

/// <summary>Run a program to completion. stdout and stderr go to files when asked, otherwise they are captured.</summary>
static class Subprocess
{
    public static SubprocessResult Run(string program, IEnumerable<string> args, string workingDir,
        IDictionary<string, string>? env = null, string? stdoutFile = null, string? stderrFile = null)
    {
        var psi = new ProcessStartInfo(program)
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        foreach (var (k, v) in env ?? new Dictionary<string, string>()) psi.Environment[k] = v;

        Process process;
        try { process = Process.Start(psi) ?? throw new InvalidOperationException("no process"); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new SubprocessResult(-1, "", $"could not start {program}: {e.Message}", Started: false);
        }

        using (process)
        {
            process.StandardInput.Close();
            var stdout = Pump(process.StandardOutput, stdoutFile);
            var stderr = Pump(process.StandardError, stderrFile);
            process.WaitForExit();
            return new SubprocessResult(process.ExitCode, stdout.Result, stderr.Result, Started: true);
        }
    }

    /// <summary>Drain a stream as it arrives, to a file when given (returning ""), otherwise into memory.</summary>
    private static async Task<string> Pump(StreamReader reader, string? file)
    {
        if (file is null) return await reader.ReadToEndAsync();
        await using var writer = new StreamWriter(file, append: false);
        var buffer = new char[8192];
        int n;
        while ((n = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            await writer.WriteAsync(buffer, 0, n);
            await writer.FlushAsync();   // a killed run still leaves everything it produced
        }
        return "";
    }
}
