using System.Text.Json;
using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

/// <summary>Runs only when PROCTOR_GATE names nb's self-contained linux-x64 publish; it needs Docker or podman (PROCTOR_GATE_ENGINE, default docker), uv and Harbor too.</summary>
sealed class GateFactAttribute : FactAttribute
{
    public GateFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PROCTOR_GATE")))
            Skip = "set PROCTOR_GATE to nb's linux-x64 publish directory (Docker or podman, uv and Harbor needed) to run the deterministic gate";
    }
}

/// <summary>
/// The deterministic gate (agent-neutral-runs decision 12): suites/gate's Mock arm run bare and the same arm run
/// through Harbor give the same nb transcript, the same counted core and the same checks.json, apart from timings and
/// the checkout's path.
/// </summary>
public class GateTests
{
    [GateFact]
    public void BareAndHarbor_AreIdentical_ApartFromTimingsAndTheCheckoutPath()
    {
        using var repo = new TestRepo();
        var (suite, id) = RunGraded(repo, "gate");
        var differences = Differences(repo.Root, id, suite);
        Assert.True(differences.Count == 0, string.Join("\n", differences));
    }

    /// <summary>A gate suite run, both arms, and graded, with the publish and the engine the environment names.</summary>
    internal static (Suite Suite, string Id) RunGraded(TestRepo repo, string suiteId)
    {
        repo.CopySuite(suiteId);
        File.WriteAllText(Path.Combine(repo.Root, "suites", "proctor.json"), JsonSerializer.Serialize(new
        {
            nb = new { path = TestRepo.NbPath, config = TestRepo.NbMockConfig },
            harbor = new
            {
                nb = Path.GetFullPath(Environment.GetEnvironmentVariable("PROCTOR_GATE")!),
                engine = Environment.GetEnvironmentVariable("PROCTOR_GATE_ENGINE") is { Length: > 0 } engine ? engine : "docker",
            },
        }));
        var problems = new List<Problem>();
        var config = Suite.LoadConfig(repo.Root, problems);
        Assert.Empty(problems);
        var suite = repo.LoadSuite(suiteId);
        var id = Runner.Start(repo.Root, suite, config, null, null, "gate", TextWriter.Null);
        Grade.Experiment(repo.Root, Runner.LoadExperiment(repo.Root, id), suite, TextWriter.Null);
        return (suite, id);
    }

    /// <summary>Every way a Harbor cell differs from its bare twin, as "task/sample: what"; empty when the gate is green.</summary>
    internal static List<string> Differences(string root, string experimentId, Suite suite)
    {
        var expDir = Layout.Experiment(root, experimentId);
        var differences = new List<string>();
        var harbor = suite.Arms.Single(a => a.IsAgent);
        var bare = suite.Arms.Single(a => !a.IsAgent);
        foreach (var c in suite.Tasks)
            foreach (var sample in Enumerable.Range(1, harbor.SamplesOrOne))
            {
                var at = $"{c.Id}/{sample}";
                var b = Layout.Cell(expDir, bare.Id!, c.Id!, sample);
                var h = Layout.Cell(expDir, harbor.Id!, c.Id!, sample);
                var (bs, hs) = (Runner.ReadStatus(b), Runner.ReadStatus(h));
                if (bs != CellStatus.Completed || hs != CellStatus.Completed)
                {
                    differences.Add($"{at}: bare {bs}, harbor {hs} ({Runner.ReadManifest(Path.Combine(h, Layout.ManifestFile)).StatusReason})");
                    continue;
                }

                // The checkout is at a host path bare and at /app in the image; a tool's output naming it is the one other expected difference.
                var bareJsonl = Untimed(File.ReadAllText(Path.Combine(b, Layout.TranscriptFile)).Replace(Layout.Work(root, experimentId, bare.Id!, c.Id!, sample), HarborCompile.Workdir));
                var harborJsonl = Untimed(File.ReadAllText(Path.Combine(h, Layout.AgentLogsDir, Layout.NbLogFile)));
                if (bareJsonl != harborJsonl) differences.Add($"{at}: nb's transcript differs\n--- bare\n{bareJsonl}\n--- harbor\n{harborJsonl}");

                var trajectory = JsonNode.Parse(File.ReadAllText(Path.Combine(b, Layout.TrajectoryFile)))!.AsObject();
                var exit = trajectory!["extra"]?["result"]?["exit_reason"]?.GetValue<string>() ?? "ok";
                var bareCore = Ingest.Count(trajectory, Arm.Nb, exit).Core! with { DurationMs = null };
                var harborCore = Runner.ReadJson<CountedCore>(Path.Combine(h, Layout.CoreFile))! with { DurationMs = null };
                var (bc, hc) = (JsonSerializer.Serialize(bareCore, Suite.JsonOptions), JsonSerializer.Serialize(harborCore, Suite.JsonOptions));
                if (bc != hc) differences.Add($"{at}: counted core differs\n--- bare\n{bc}\n--- harbor\n{hc}");

                var (bk, hk) = (File.ReadAllText(Path.Combine(b, Layout.ChecksFile)), File.ReadAllText(Path.Combine(h, Layout.ChecksFile)));
                if (bk != hk) differences.Add($"{at}: checks.json differs\n--- bare\n{bk}\n--- harbor\n{hk}");
            }
        return differences;
    }

    /// <summary>nb's JSONL without the trailer's timings, which are the only fields two identical runs may differ in.</summary>
    static string Untimed(string jsonl) => string.Join("\n", jsonl.Split('\n').Where(l => l.Trim().Length > 0).Select(line =>
    {
        var ev = JsonNode.Parse(line)!.AsObject();
        if (ev["type"]?.GetValue<string>() == "result") { ev.Remove("duration_ms"); ev.Remove("provider_ms"); }
        return ev.ToJsonString();
    }));
}

/// <summary>
/// The oracle's gate (plans/oracle-in-proctor.md, *The gate*): suites/gate-oracle's Mock arm bare, with nb's in-process
/// oracle, and through Harbor, with the simulated user over the ACP bridge, give the same counted core, the same user
/// turns (text, source, keys) and the same checks.json, for each of the endings ok, oracle_miss and oracle_budget. The
/// one known difference is the judge's tokens: bare, nb charges them to the run; bridged, they are the manifest's.
/// </summary>
public class OracleGateTests
{
    [GateFact]
    public void BareAndBridged_Agree_ApartFromTheJudgesTokens()
    {
        using var repo = new TestRepo();
        var (suite, id) = GateTests.RunGraded(repo, "gate-oracle");
        var differences = Differences(repo.Root, id, suite);
        Assert.True(differences.Count == 0, string.Join("\n", differences));
    }

    /// <summary>The answers each gate-oracle task's sheet scripts before its ending (oracle.turns is 2).</summary>
    static readonly Dictionary<string, int> Answers = new() { ["asks-twice"] = 2, ["misses"] = 0, ["over-budget"] = 2 };

    internal static List<string> Differences(string root, string experimentId, Suite suite)
    {
        var expDir = Layout.Experiment(root, experimentId);
        var differences = new List<string>();
        var harbor = suite.Arms.Single(a => a.IsAgent);
        var bare = suite.Arms.Single(a => !a.IsAgent);
        foreach (var c in suite.Tasks)
        {
            var at = c.Id!;
            var b = Layout.Cell(expDir, bare.Id!, c.Id!, 1);
            var h = Layout.Cell(expDir, harbor.Id!, c.Id!, 1);
            var (bs, hs) = (Runner.ReadStatus(b), Runner.ReadStatus(h));
            var manifest = Runner.ReadManifest(Path.Combine(h, Layout.ManifestFile));
            if (bs != CellStatus.Completed || hs != CellStatus.Completed)
            {
                differences.Add($"{at}: bare {bs}, harbor {hs} ({manifest.StatusReason})");
                continue;
            }

            var trajectory = JsonNode.Parse(File.ReadAllText(Path.Combine(b, Layout.TrajectoryFile)))!.AsObject();
            var exit = trajectory["extra"]?["result"]?["exit_reason"]?.GetValue<string>() ?? "ok";
            var bareCore = Ingest.Count(trajectory, Arm.Nb, exit).Core! with { DurationMs = null };
            var judge = manifest.Oracle;
            if (judge is null) { differences.Add($"{at}: the harbor cell's manifest has no oracle"); continue; }
            bareCore = bareCore with
            {
                InputTokens = bareCore.InputTokens - judge.JudgeInputTokens,
                OutputTokens = bareCore.OutputTokens - judge.JudgeOutputTokens,
            };
            var harborCore = Runner.ReadJson<CountedCore>(Path.Combine(h, Layout.CoreFile))! with { DurationMs = null };
            var (bc, hc) = (JsonSerializer.Serialize(bareCore, Suite.JsonOptions), JsonSerializer.Serialize(harborCore, Suite.JsonOptions));
            if (bc != hc) differences.Add($"{at}: counted core differs, the judge's tokens taken out of the bare one\n--- bare\n{bc}\n--- harbor\n{hc}");

            // Not only alike: each ending is the one its task scripts, with the answers it takes to get there.
            var expected = c.Expect!["exit_reason"]!.GetValue<string>();
            var answers = Transcript.Read(h).UserTurns.Count(u => u.Source == "oracle");
            if (harborCore.ExitReason != expected || answers != Answers[c.Id!])
                differences.Add($"{at}: harbor ended {harborCore.ExitReason} after {answers} answers; the task scripts {expected} after {Answers[c.Id!]}");

            string Turns(string cell) => string.Join("\n", Transcript.Read(cell).UserTurns.Select(u => $"{u.Source ?? "-"} [{string.Join(",", u.Keys ?? [])}] {u.Text}"));
            var (bt, ht) = (Turns(b), Turns(h));
            if (bt != ht) differences.Add($"{at}: user turns differ\n--- bare\n{bt}\n--- harbor\n{ht}");

            var (bk, hk) = (File.ReadAllText(Path.Combine(b, Layout.ChecksFile)), File.ReadAllText(Path.Combine(h, Layout.ChecksFile)));
            if (bk != hk) differences.Add($"{at}: checks.json differs\n--- bare\n{bk}\n--- harbor\n{hk}");
        }
        return differences;
    }
}
