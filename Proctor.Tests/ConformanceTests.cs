using System.Text.Json;
using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

/// <summary>Runs only when PROCTOR_CONFORMANCE is set; it needs Docker or podman (proctor.json harbor.engine), uv and Harbor, and network to build the images.</summary>
sealed class ConformanceFactAttribute : FactAttribute
{
    public ConformanceFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PROCTOR_CONFORMANCE")))
            Skip = "set PROCTOR_CONFORMANCE=1 (Docker or podman, uv and Harbor needed) to run every vendor agent against the stub model";
    }
}

/// <summary>
/// Harbor conformance (project/plans/harbor-conformance.md, rows 3 to 9): suites/conformance runs each vendor agent at
/// its pin against the stub fixture's scripted model, which costs nothing, and every cell must come back as the stub
/// scripted it: ok completes with one shell call, the verifier's check and every offered tool mapped; fail is an
/// outage; hang is a completed run that hit its time budget.
/// </summary>
public class ConformanceTests
{
    const string Stub = "http://localhost:8765";

    [ConformanceFact]
    public void EveryVendorAgent_RunsAndReadsBack_AsTheStubScriptedIt()
    {
        using var repo = new TestRepo();
        repo.CopySuite("conformance");
        File.WriteAllText(Path.Combine(repo.Root, "suites", "proctor.json"), JsonSerializer.Serialize(new
        {
            nb = new { path = TestRepo.NbPath, config = TestRepo.NbMockConfig },
            agents = new Dictionary<string, object>
            {
                ["claude-code"] = new { base_url = Stub, api_key = "stub" },
                ["codex"] = new { base_url = $"{Stub}/v1", api_key = "stub" },
                ["qwen-code"] = new { base_url = $"{Stub}/v1", api_key = "stub" },
            },
        }));
        var config = Suite.LoadConfig(repo.Root, []);
        var suite = repo.LoadSuite("conformance");
        var id = Runner.Start(repo.Root, suite, config, null, null, "conformance", TextWriter.Null);
        Grade.Experiment(repo.Root, Runner.LoadExperiment(repo.Root, id), suite, TextWriter.Null);

        var misses = Misses(repo.Root, id, suite);
        Assert.True(misses.Count == 0, string.Join("\n", misses));
    }

    /// <summary>
    /// Row 10: each code-change task, compiled as proctor compiles it, with its reference solution (reference/<task>.*) as the
    /// task's solution/ and Harbor's oracle agent in place of a model, passes every script check in the verifier.
    /// That proves the image and the offline verifier for the suite's own tasks; no model, no network in the agent phase.
    /// </summary>
    [ConformanceFact]
    public void EveryCodeChangeTask_PassesItsChecks_WithItsReferenceSolution()
    {
        using var repo = new TestRepo();
        repo.CopySuite("code-change");
        var suite = repo.LoadSuite("code-change");
        var arm = suite.Arms.First(a => a.IsVendor);
        var expDir = Path.Combine(repo.Root, "runs", "oracle");
        HarborCompile.Tasks(expDir, suite, "oracle", _ => new AgentRoute(null, null));

        var misses = new List<string>();
        foreach (var c in suite.Tasks)
        {
            var taskDir = Layout.HarborTask(expDir, arm.Id!, c.Id!);
            var solution = Path.Combine(taskDir, "solution");
            Directory.CreateDirectory(solution);
            foreach (var file in Directory.GetFiles(Path.Combine(suite.Dir, "reference"), $"{c.Id}.*")) File.Copy(file, Path.Combine(solution, Path.GetFileName(file)));
            File.WriteAllText(Path.Combine(solution, "solve.sh"), $"#!/usr/bin/env bash\nset -euo pipefail\nbash \"$(dirname \"$0\")/{c.Id}.sh\" {HarborCompile.Workdir}\n");

            var harborDir = Path.Combine(expDir, "oracle-jobs", c.Id!);
            Directory.CreateDirectory(harborDir);
            var job = HarborCompile.Job(arm, taskDir, Path.Combine(harborDir, "jobs"), new AgentRoute(null, null));
            job["agents"] = new JsonArray(new JsonObject { ["name"] = "oracle" });
            Runner.WriteJson(Path.Combine(harborDir, Layout.HarborJobFile), job);
            var run = Subprocess.Run("uv", HarborRun.UvArgs(suite.Def.Harbor!.Version), harborDir,
                new Dictionary<string, string> { ["HARBOR_TELEMETRY"] = "off", ["NO_COLOR"] = "1" }, timeout: TimeSpan.FromMinutes(30));

            var reward = Directory.GetFiles(Path.Combine(harborDir, "jobs"), Layout.RewardFile, SearchOption.AllDirectories).FirstOrDefault();
            if (reward is null) { misses.Add($"{c.Id}: no reward.json (Harbor exited {run.ExitCode}: {run.Stderr.Trim().Split('\n').LastOrDefault()})"); continue; }
            foreach (var (check, value) in JsonNode.Parse(File.ReadAllText(reward))!.AsObject())
                if (value?.GetValue<double>() != 1) misses.Add($"{c.Id}: {check} is {value?.ToJsonString()} with the reference solution");
        }
        Assert.True(misses.Count == 0, string.Join("\n", misses));
    }

    /// <summary>Every way a cell differs from the stub's script, as "arm/task: what"; empty when conformance is green.</summary>
    internal static List<string> Misses(string root, string experimentId, Suite suite)
    {
        var expDir = Layout.Experiment(root, experimentId);
        var misses = new List<string>();
        foreach (var arm in suite.Arms)
        {
            void Miss(string task, string what) => misses.Add($"{arm.Id}/{task}: {what}");
            string Cell(string task) => Layout.Cell(expDir, arm.Id!, task, 1);
            string Reason(string task) => Runner.ReadManifest(Path.Combine(Cell(task), Layout.ManifestFile)).StatusReason ?? "";
            CountedCore? Core(string task) => File.Exists(Path.Combine(Cell(task), Layout.CoreFile)) ? Runner.ReadJson<CountedCore>(Path.Combine(Cell(task), Layout.CoreFile)) : null;

            if (Runner.ReadStatus(Cell("ok")) is var ok && ok != CellStatus.Completed) Miss("ok", $"{ok}: {Reason("ok")}");
            else
            {
                var core = Core("ok")!;
                if (core.ToolCalls != 1 || core.Kinds.GetValueOrDefault("shell") != 1) Miss("ok", $"counted {core.ToolCalls} calls ({string.Join(", ", core.Kinds.Select(k => $"{k.Key}={k.Value}"))}), not one shell call");
                var reward = JsonNode.Parse(File.ReadAllText(Path.Combine(Cell("ok"), Layout.RewardFile)))!;
                if (reward["made-file"]?.GetValue<double>() != 1) Miss("ok", $"the verifier's made-file is {reward["made-file"]?.ToJsonString() ?? "missing"}");
                var checks = Grade.ReadChecks(Cell("ok"));
                if (checks is null) Miss("ok", "not graded");
                else if (Grade.Pass(checks, suite.Grading.Pass!) != true)
                    Miss("ok", "does not pass: " + string.Join("; ", checks.Where(k => k.Value.Result != Verdict.Pass).Select(k => $"{k.Key}={k.Value.Result} ({k.Value.Reason})")));
                var answer = Transcript.Read(Cell("ok")).Answer ?? "";
                if (!answer.StartsWith("tools: ")) Miss("ok", $"the answer is not the stub's tool list: '{answer}'");
                else
                {
                    var unmapped = answer["tools: ".Length..].Split(", ").Where(t => !Ingest.Kinds[arm.Agent!].ContainsKey(t)).ToList();
                    if (unmapped.Count > 0) Miss("ok", $"tools the agent offers that Ingest.Kinds lacks: {string.Join(", ", unmapped)}");
                }
            }

            if (Runner.ReadStatus(Cell("fail")) is var fail && fail != CellStatus.Failed) Miss("fail", $"{fail}, not failed: an outage must never read as a run");

            if (Runner.ReadStatus(Cell("hang")) is var hang && hang != CellStatus.Completed) Miss("hang", $"{hang}: {Reason("hang")}");
            else if (Core("hang")?.ExitReason is var exit && exit != "time_budget") Miss("hang", $"exit reason {exit}, not time_budget");
        }
        return misses;
    }
}
