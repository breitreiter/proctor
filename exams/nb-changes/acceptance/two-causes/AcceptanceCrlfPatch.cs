using nb.Shell;
using nb.Shell.ApplyPatch;

namespace nb.Tests.Acceptance;

public class AcceptanceCrlfPatch : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"nb-acceptance-{Guid.NewGuid():N}");
    readonly FileReadTracker _tracker = new();

    public AcceptanceCrlfPatch() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    string Patched(string name, string content, string patch)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        _tracker.RecordRead(path);
        var ops = PatchParser.Parse(patch);
        PatchApplier.Apply(PatchApplier.BuildPreview(ops, _dir, _tracker), ops, _dir, _tracker);
        return File.ReadAllText(path);
    }

    [Fact]
    public void LastLinesOfACrlfFile_ArePatched_AndEveryEndingStaysCrlf()
    {
        var patch = """
            *** Begin Patch
            *** Update File: Tail.cs
             int Last()
             {
            -    return 1;
            +    return 2;
             }
            *** End Patch
            """;
        var got = Patched("Tail.cs", "int First()\r\n{\r\n    return 0;\r\n}\r\n\r\nint Last()\r\n{\r\n    return 1;\r\n}\r\n", patch);
        Assert.Equal("int First()\r\n{\r\n    return 0;\r\n}\r\n\r\nint Last()\r\n{\r\n    return 2;\r\n}\r\n", got);
    }

    [Fact]
    public void LastLinesOfAnLfFile_ArePatched()
    {
        var patch = """
            *** Begin Patch
            *** Update File: tail.txt
             two
            -three
            +THREE
            *** End Patch
            """;
        Assert.Equal("one\ntwo\nTHREE\n", Patched("tail.txt", "one\ntwo\nthree\n", patch));
    }

    [Fact]
    public void MiddleOfACrlfFile_KeepsCrlfOnTheLastLine()
    {
        var patch = """
            *** Begin Patch
            *** Update File: mid.txt
            -one
            +ONE
             two
            *** End Patch
            """;
        Assert.Equal("ONE\r\ntwo\r\nthree\r\n", Patched("mid.txt", "one\r\ntwo\r\nthree\r\n", patch));
    }

    [Fact]
    public void SeekFindsAPatternThatEndsTheFile()
    {
        Assert.Equal(2, SeekSequence.Find(new[] { "a", "b", "c", "d" }, new[] { "c", "d" }, 0));
        Assert.Equal(0, SeekSequence.Find(new[] { "a" }, new[] { "a" }, 0));
        Assert.Equal(-1, SeekSequence.Find(new[] { "a", "b" }, new[] { "b", "c" }, 0));
    }
}
