using System.Text.Json.Nodes;
using Auspex;

namespace Auspex.Tests;

/// <summary>
/// nb writes its ATIF trajectory beside its JSONL, keeping under `extra` what the JSONL carries. Before grading bare
/// attempts from the trajectory (check-evidence.md, C3: evidence by ATIF step id), every window a check reads must come
/// out the same from both, on every captured pair in fixtures/nb-run.
/// </summary>
public class TrajectoryParityTests
{
    public static TheoryData<string> Pairs()
    {
        var data = new TheoryData<string>();
        foreach (var f in Directory.GetFiles(Dir, "*.trajectory.json").Order(StringComparer.Ordinal))
            data.Add(Path.GetFileName(f)[..^".trajectory.json".Length]);
        return data;
    }

    static string Dir => Path.Combine(TestRepo.SourceRoot, "Auspex.Tests", "fixtures", "nb-run");

    [Theory]
    [MemberData(nameof(Pairs))]
    public void TheTrajectoryReadsTheSameAsTheJsonl(string name)
    {
        var jsonl = Transcript.Parse(File.ReadAllText(Path.Combine(Dir, name + ".jsonl")));
        var atif = Transcript.FromAtif(JsonNode.Parse(File.ReadAllText(Path.Combine(Dir, name + ".trajectory.json")))!.AsObject(), core: null);

        Assert.Equal(jsonl.Trailer, atif.Trailer);
        Assert.Equal(jsonl.Answer, atif.Answer);
        Assert.Equal(jsonl.AnswerJson?.ToJsonString(), atif.AnswerJson?.ToJsonString());
        Assert.Equal(jsonl.UserTurns, atif.UserTurns);
        Assert.Equal(jsonl.LoopNudged, atif.LoopNudged);
        Assert.Equal(Calls(jsonl), Calls(atif));
        Assert.Equal(Results(jsonl), Results(atif));
    }

    /// <summary>Opt in with PROCTOR_PARITY_DIR=dir: every attempt under it holding both nb's transcript.jsonl and trajectory.json reads the same both ways.</summary>
    [Fact]
    public void RealAttempts_ReadTheSameBothWays()
    {
        if (Environment.GetEnvironmentVariable("PROCTOR_PARITY_DIR") is not { Length: > 0 } root) return;
        var differ = new List<string>();
        var n = 0;
        foreach (var jsonlFile in Directory.EnumerateFiles(root, Layout.TranscriptFile, SearchOption.AllDirectories))
        {
            var trajectoryFile = Path.Combine(Path.GetDirectoryName(jsonlFile)!, Layout.TrajectoryFile);
            if (!File.Exists(trajectoryFile)) continue;
            n++;
            var jsonl = Transcript.Parse(File.ReadAllText(jsonlFile));
            var atif = Transcript.FromAtif(JsonNode.Parse(File.ReadAllText(trajectoryFile))!.AsObject(), core: null);
            var what = new List<string>();
            if (!Equals(jsonl.Trailer, atif.Trailer)) what.Add($"trailer {jsonl.Trailer} vs {atif.Trailer}");
            if (jsonl.Answer != atif.Answer) what.Add("answer");
            if (jsonl.AnswerJson?.ToJsonString() != atif.AnswerJson?.ToJsonString()) what.Add("answer json");
            if (!jsonl.UserTurns.SequenceEqual(atif.UserTurns)) what.Add("user turns");
            if (!Calls(jsonl).SequenceEqual(Calls(atif))) what.Add($"tool calls ({jsonl.ToolCalls.Count} vs {atif.ToolCalls.Count})");
            if (!Results(jsonl).SequenceEqual(Results(atif))) what.Add($"tool results ({jsonl.ToolResults.Count} vs {atif.ToolResults.Count})");
            if (what.Count > 0) differ.Add($"{Path.GetRelativePath(root, Path.GetDirectoryName(jsonlFile)!)}: {string.Join("; ", what)}");
        }
        Assert.True(n > 0, "no attempt with both files under " + root);
        Assert.True(differ.Count == 0, $"{differ.Count} of {n} differ:\n" + string.Join("\n", differ.Take(20)));
    }

    static List<string> Calls(Transcript t) =>
        [.. t.ToolCalls.Select(c => $"{c.Id} {c.Name} {c.Arguments?.ToJsonString()} {c.Approved} {c.ApprovalReason}")];

    static List<string> Results(Transcript t) =>
        [.. t.ToolResults.Select(r => $"{r.Id} {r.IsError} {r.Result?.ToJsonString()}\n{r.Output}")];
}
