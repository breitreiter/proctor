using Proctor;

namespace Proctor.Tests;

public class EnvFileTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("proctor-env-").FullName;
    // Process-wide state: every test uses names of its own so parallel tests cannot collide.
    readonly string prefix = "PROCTOR_TEST_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant() + "_";
    readonly List<string> touched = [];

    string Var(string name) { touched.Add(prefix + name); return prefix + name; }

    string Write(string text)
    {
        var path = Path.Combine(dir, "env");
        File.WriteAllText(path, text);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return path;
    }

    public void Dispose()
    {
        foreach (var name in touched) Environment.SetEnvironmentVariable(name, null);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Parse_TakesCommentsExportAndQuotes()
    {
        var path = Write("""
            # keys for proctor

            GEMINI_API_KEY=abc123
            export OPENROUTER_API_KEY = "sk-or-v1 with space"
            SINGLE='it''s'
            EMPTY=
            WITH_EQUALS=a=b=c
            """);
        Assert.Equal(
            [("GEMINI_API_KEY", "abc123"), ("OPENROUTER_API_KEY", "sk-or-v1 with space"), ("SINGLE", "it''s"), ("EMPTY", ""), ("WITH_EQUALS", "a=b=c")],
            EnvFile.Parse(path));
    }

    [Theory]
    [InlineData("just some text")]
    [InlineData("=value")]
    [InlineData("1ABC=value")]
    [InlineData("BAD-NAME=value")]
    public void Parse_RefusesALineThatIsNotNameEqualsValue_WithItsLineNumber(string bad)
    {
        var path = Write($"OK=1\n{bad}\n");
        var e = Assert.Throws<ProctorException>(() => EnvFile.Parse(path));
        Assert.StartsWith($"{path}:2:", e.Message);
    }

    [Fact]
    public void Load_SetsWhatTheEnvironmentLacks_AndLeavesWhatItHas()
    {
        var fromFile = Var("FROM_FILE");
        var already = Var("ALREADY");
        Environment.SetEnvironmentVariable(already, "from the shell");
        var path = Write($"{fromFile}=from the file\n{already}=from the file\n");

        Assert.Equal([fromFile], EnvFile.Load(path));
        Assert.Equal("from the file", Environment.GetEnvironmentVariable(fromFile));
        Assert.Equal("from the shell", Environment.GetEnvironmentVariable(already));
    }

    [Fact]
    public void Load_AMissingFileSetsNothing() =>
        Assert.Empty(EnvFile.Load(Path.Combine(dir, "no-such-file")));

    [Fact]
    public void ALoadedKey_ResolvesInAJudgeEndpoint()
    {
        var key = Var("JUDGE_KEY");
        EnvFile.Load(Write($"{key}=secret\n"));
        var judge = new JudgeDef("chat", "http://judge/v1", "m", $"${{{key}}}", null, null);
        Assert.Equal("secret", judge.ResolvedKey("j"));
    }

    [Fact]
    public void DefaultPath_IsTheOverrideWhenSet_ElseUnderTheConfigDirectory()
    {
        var before = Environment.GetEnvironmentVariable(EnvFile.OverrideVariable);
        try
        {
            Environment.SetEnvironmentVariable(EnvFile.OverrideVariable, "/somewhere/env");
            Assert.Equal("/somewhere/env", EnvFile.DefaultPath());
            Environment.SetEnvironmentVariable(EnvFile.OverrideVariable, null);
            Assert.EndsWith(Path.Combine("proctor", "env"), EnvFile.DefaultPath());
        }
        finally { Environment.SetEnvironmentVariable(EnvFile.OverrideVariable, before); }
    }

    [Fact]
    public void Set_CreatesTheFileOnlyTheUserCanRead()
    {
        var path = Path.Combine(dir, "nested", "env");
        EnvFile.Set(path, "A_API_KEY", "secret-1");
        Assert.Equal([("A_API_KEY", "secret-1")], EnvFile.Parse(path));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Set_ReplacesTheVariablesLine_AndKeepsEveryOtherLine()
    {
        var path = Write("# mine\nexport A_API_KEY=old\nB=2\n");
        EnvFile.Set(path, "A_API_KEY", "new");
        EnvFile.Set(path, "C_API_KEY", "3");
        Assert.Equal("# mine\nA_API_KEY=new\nB=2\nC_API_KEY=3\n", File.ReadAllText(path));
    }

    [Fact]
    public void Set_RefusesAValueOnTwoLines() =>
        Assert.Throws<ProctorException>(() => EnvFile.Set(Path.Combine(dir, "env"), "A", "x\nB=y"));
}
