namespace Proctor;

/// <summary>
/// The user's env file, <c>~/.config/proctor/env</c>: <c>NAME=value</c> lines that every verb loads into
/// its environment at startup, so a <c>${VAR}</c> in nb's config, a judge or an agent route resolves without an
/// export. A variable already in the environment wins. The file is the user's, never the repository's, so a
/// key in it can be neither committed nor copied into a work directory.
/// </summary>
static class EnvFile
{
    public const string OverrideVariable = "PROCTOR_ENV_FILE";

    /// <summary><c>$PROCTOR_ENV_FILE</c>, else <c>$XDG_CONFIG_HOME/proctor/env</c>, else <c>~/.config/proctor/env</c> (on Windows, <c>%LOCALAPPDATA%\proctor\env</c>: local, not roaming, so a key never syncs to a domain profile server).</summary>
    public static string DefaultPath()
    {
        if (Environment.GetEnvironmentVariable(OverrideVariable) is { Length: > 0 } explicitPath) return explicitPath;
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "proctor", "env");
        var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg
            ? xdg
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(config, "proctor", "env");
    }

    /// <summary>
    /// The file's variables, in order. Blank lines and <c>#</c> comments are skipped; <c>export </c> before a name is
    /// allowed; a value in matching single or double quotes loses them, with nothing unescaped. A line that is not
    /// <c>NAME=value</c> is a <see cref="ProctorException"/> naming the file and line, never skipped, since a key
    /// that silently fails to load surfaces later as a provider's 401.
    /// </summary>
    public static List<(string Name, string Value)> Parse(string path)
    {
        var vars = new List<(string, string)>();
        var lines = File.ReadAllLines(path);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("export ")) line = line["export ".Length..].TrimStart();
            var eq = line.IndexOf('=');
            var name = eq > 0 ? line[..eq].Trim() : "";
            if (!IsName(name))
                throw new ProctorException($"{path}:{i + 1}: expected NAME=value, where NAME is letters, digits and underscores");
            var value = line[(eq + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0]) value = value[1..^1];
            vars.Add((name, value));
        }
        return vars;
    }

    /// <summary>Sets each of the file's variables that the environment does not already hold; returns the names set. A missing file sets nothing.</summary>
    public static List<string> Load(string path)
    {
        if (!File.Exists(path)) return [];
        WarnIfShared(path);
        var set = new List<string>();
        foreach (var (name, value) in Parse(path))
        {
            if (Environment.GetEnvironmentVariable(name) is not null) continue;
            Environment.SetEnvironmentVariable(name, value);
            set.Add(name);
        }
        return set;
    }

    /// <summary>The file holds keys: say so when others can read it, as ssh does, but do not refuse.</summary>
    static void WarnIfShared(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        var mode = File.GetUnixFileMode(path);
        if ((mode & (UnixFileMode.GroupRead | UnixFileMode.OtherRead | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0)
            Console.Error.WriteLine($"warning: {path} holds keys and others can read it; chmod 600 {path}");
    }

    static bool IsName(string s) =>
        s.Length > 0 && (char.IsAsciiLetter(s[0]) || s[0] == '_') && s.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
}
