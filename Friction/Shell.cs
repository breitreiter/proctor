using System.Text;
using System.Text.RegularExpressions;

namespace Auspex;

/// <summary>
/// A shell command as the friction rules read it: split into actions, one per pipeline, named by the pipeline's first
/// command. Not a shell parser: quoting is followed far enough to find where one command ends and what its words are.
/// Paths and globs here are the container's, so they are POSIX whatever the host is.
/// </summary>
static class Shell
{
    const string Punctuation = "();<>|&";
    static readonly HashSet<string> Separators = ["&&", "||", ";", "|", "&", ";;", "|&"];
    static readonly HashSet<string> Prefix = ["sudo", "time", "env", "command", "exec", "nohup", "stdbuf"];
    static readonly HashSet<string> Control = ["for", "do", "done", "if", "then", "else", "elif", "fi", "while", "until", "(", ")", "{", "}", "!", "[", "[["];
    static readonly Regex Heredoc = new(@"<<-?\s*'?""?(\w+)'?""?[^\n]*\n.*?\n\1(?=\n|$)", RegexOptions.Singleline);
    static readonly Regex Function = new(@"(?:^|(?<=[;\n]))[ \t]*(?:function\s+)?([A-Za-z_]\w*)\s*\(\)\s*\{(.*?)[;\n]\s*\}", RegexOptions.Singleline);
    static readonly Regex Assignment = new(@"^\w+=");

    /// <summary>The command with each heredoc's body replaced by a placeholder, so a script's text is not read as commands.</summary>
    public static string StripHeredocs(string command) => Heredoc.Replace(command, "<<HEREDOC");

    /// <summary>
    /// The command line as shell words. Quotes group and are removed, a backslash keeps the next character, a run of
    /// ();&lt;&gt;|&amp; is one word, a # at the start of a word comments out the rest of the line, and a newline is a ';'.
    /// Null when a quote is left open.
    /// </summary>
    public static List<string>? Words(string command)
    {
        var words = new List<string>();
        StringBuilder? word = null;
        void End() { if (word is not null) { words.Add(word.ToString()); word = null; } }
        int i = 0, n = command.Length;
        while (i < n)
        {
            var ch = command[i];
            if (ch == '\n') { End(); words.Add(";"); i++; }
            else if (ch is ' ' or '\t' or '\r') { End(); i++; }
            else if (ch == '#' && word is null) { while (i < n && command[i] != '\n') i++; }
            else if (Punctuation.Contains(ch))
            {
                End();
                var j = i;
                while (j < n && Punctuation.Contains(command[j])) j++;
                words.Add(command[i..j]);
                i = j;
            }
            else if (ch == '\\')
            {
                word ??= new();
                if (i + 1 < n) word.Append(command[i + 1]);
                i += 2;
            }
            else if (ch == '\'')
            {
                var j = command.IndexOf('\'', i + 1);
                if (j < 0) return null;
                (word ??= new()).Append(command, i + 1, j - i - 1);
                i = j + 1;
            }
            else if (ch == '"')
            {
                word ??= new();
                i++;
                while (i < n && command[i] != '"')
                {
                    if (command[i] == '\\' && i + 1 < n && command[i + 1] is '"' or '\\') i++;
                    word.Append(command[i]);
                    i++;
                }
                if (i >= n) return null;
                i++;
            }
            else { (word ??= new()).Append(ch); i++; }
        }
        End();
        return words;
    }

    /// <summary>The words of each action: one per pipeline, the first command's words only, without `sudo`, `VAR=x` and loop keywords in front.</summary>
    public static IEnumerable<List<string>> Actions(string command)
    {
        command = Function.Replace(StripHeredocs(command), " ");
        var words = Words(command) ?? SplitWhitespace(command.Replace("\n", " ; "));
        var action = new List<string>();
        var piped = false;
        foreach (var word in words.Append(";"))
        {
            if (!Separators.Contains(word)) { action.Add(word); continue; }
            while (action.Count > 0 && (Control.Contains(action[0]) || Prefix.Contains(action[0]) || Assignment.IsMatch(action[0]) || (action[0] == "timeout" && action.Count > 2)))
                action.RemoveRange(0, Math.Min(action.Count, action[0] == "timeout" ? 2 : 1));
            if (action.Count > 0 && !piped) yield return action;
            action = [];
            piped = word is "|" or "|&";
        }
    }

    public static List<string> SplitWhitespace(string text)
    {
        var parts = new List<string>();
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var space = i == text.Length || char.IsWhiteSpace(text[i]);
            if (!space && start < 0) start = i;
            if (space && start >= 0) { parts.Add(text[start..i]); start = -1; }
        }
        return parts;
    }

    /// <summary>The text with every whitespace character removed: what a quote is compared in.</summary>
    public static string Squash(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text) if (!char.IsWhiteSpace(ch)) sb.Append(ch);
        return sb.ToString();
    }

    public static string BaseName(string path) => path[(path.LastIndexOf('/') + 1)..];

    public static string Join(string directory, string path) =>
        path.StartsWith('/') ? path : directory.Length == 0 || directory.EndsWith('/') ? directory + path : directory + "/" + path;

    /// <summary>A POSIX path with `.`, `..` and doubled slashes resolved, as the container would resolve it.</summary>
    public static string Normalise(string path)
    {
        if (path.Length == 0) return ".";
        var slashes = path.StartsWith('/') ? (path.StartsWith("//") && !path.StartsWith("///") ? 2 : 1) : 0;
        var kept = new List<string>();
        foreach (var part in path.Split('/'))
        {
            if (part is "" or ".") continue;
            if (part != ".." || (slashes == 0 && kept.Count == 0) || (kept.Count > 0 && kept[^1] == "..")) kept.Add(part);
            else if (kept.Count > 0) kept.RemoveAt(kept.Count - 1);
        }
        var result = new string('/', slashes) + string.Join('/', kept);
        return result.Length == 0 ? "." : result;
    }

    public static string Resolve(string directory, string path) => Normalise(Join(directory, path));

    static readonly Dictionary<string, Regex> Globs = [];

    /// <summary>A glob in which `*` crosses directories, as a declaration's path patterns are written: `/opt/tool/docs/*` is everything under it.</summary>
    public static bool Matches(string text, string glob)
    {
        Regex regex;
        lock (Globs)
            if (!Globs.TryGetValue(glob, out regex!)) Globs[glob] = regex = new Regex(@"\A(?:" + GlobPattern(glob) + @")\z", RegexOptions.Singleline);
        return regex.IsMatch(text);
    }

    static string GlobPattern(string glob)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < glob.Length; i++)
        {
            var ch = glob[i];
            if (ch == '*') sb.Append(".*");
            else if (ch == '?') sb.Append('.');
            else if (ch == '[' && glob.IndexOf(']', i + 2 <= glob.Length ? Math.Min(i + 2, glob.Length - 1) : i) is var close && close > i + 1)
            {
                var body = glob[(i + 1)..close].Replace(@"\", @"\\");
                sb.Append('[').Append(body.StartsWith('!') ? "^" + body[1..] : body.StartsWith('^') ? @"\" + body : body).Append(']');
                i = close;
            }
            else sb.Append(Regex.Escape(ch.ToString()));
        }
        return sb.ToString();
    }
}
