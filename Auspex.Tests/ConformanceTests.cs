using System.Text.Json;
using System.Text.Json.Nodes;
using Auspex;

namespace Auspex.Tests;

/// <summary>
/// Runs only when AUSPEX_CONFORMANCE is set; it needs Docker or podman, uv and Harbor, and network to build the images.
/// `1` takes harbor.uv and harbor.engine from this repository's exams/auspex.json; a path takes them from that file.
/// </summary>
sealed class ConformanceFactAttribute : FactAttribute
{
    public ConformanceFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AUSPEX_CONFORMANCE")))
            Skip = "set AUSPEX_CONFORMANCE=1 (Docker or podman, uv and Harbor needed) to run every vendor agent against the stub model";
    }
}

/// <summary>
/// Harbor conformance (project/archive/harbor-conformance.md, rows 3 to 9): exams/conformance runs each vendor agent at
/// its pin against the stub fixture's scripted model, which costs nothing, and every try must come back as the stub
/// scripted it: ok completes with one shell call, the verifier's check and every offered tool mapped; fail is an
/// outage; hang is a completed run that hit its time budget.
/// </summary>
public class ConformanceTests
{
    const string Stub = "http://localhost:8765";

    /// <summary>The Harbor conformance runs on: harbor.uv and harbor.engine of the auspex.json AUSPEX_CONFORMANCE names.</summary>
    static (string Uv, string Engine) MachineHarbor()
    {
        var named = Environment.GetEnvironmentVariable("AUSPEX_CONFORMANCE");
        var file = named is null or "1" ? Path.Combine(TestRepo.SourceRoot, "exams", "auspex.json") : Path.GetFullPath(named);
        var harbor = File.Exists(file) ? JsonNode.Parse(File.ReadAllText(file))?["harbor"] : null;
        return (harbor?["uv"]?.GetValue<string>() ?? "uv", harbor?["engine"]?.GetValue<string>() ?? "docker");
    }

    [ConformanceFact]
    public void EveryVendorAgent_RunsAndReadsBack_AsTheStubScriptedIt()
    {
        using var repo = new TestRepo();
        repo.CopyExam("conformance");
        var (uv, engine) = MachineHarbor();
        File.WriteAllText(Path.Combine(repo.Root, "exams", "auspex.json"), JsonSerializer.Serialize(new
        {
            nb = new { path = TestRepo.NbPath, config = TestRepo.NbMockConfig },
            agents = new Dictionary<string, object>
            {
                ["claude-code"] = new { base_url = Stub, api_key = "stub" },
                ["codex"] = new { base_url = $"{Stub}/v1", api_key = "stub" },
                ["qwen-code"] = new { base_url = $"{Stub}/v1", api_key = "stub" },
            },
            harbor = new { uv, engine },
        }));
        var config = Exam.LoadConfig(repo.Root, []);
        var (exam, keys) = Verbs.ForRun(repo.Root, repo.LoadExam("conformance"), null, null, null);
        Runner.Run(repo.Root, exam, config, null, null, "conformance", TextWriter.Null, keys);
        Grade.Attempts(repo.Root, exam, Bank.Select(repo.Root, exam).Current, TextWriter.Null);

        var misses = Misses(repo.Root, exam);
        Assert.True(misses.Count == 0, string.Join("\n", misses));
    }

    /// <summary>
    /// Row 10: each code-change stage, compiled as proctor compiles it, with its reference solution (reference/<stage>.*) as its
    /// step's solution/ and Harbor's oracle agent in place of a model, passes every script check in the verifier.
    /// That proves the image and the offline verifier for the exam's own stages; no model, no network in the agent phase.
    /// </summary>
    [ConformanceFact]
    public void EveryCodeChangeStage_PassesItsChecks_WithItsReferenceSolution()
    {
        using var repo = new TestRepo();
        repo.CopyExam("code-change");
        var exam = repo.LoadExam("code-change");
        var (uv, engine) = MachineHarbor();
        var entryDir = Path.Combine(repo.Root, "log", "oracle");
        // One vendor's agent in each repository: the image is the fixture's, whoever the assistant is.
        var pairs = exam.Assistants.Where(v => v.IsVendor).GroupBy(v => exam.Axes!.Of(v.Id!)![exam.Axes.WorldAxis!]).Select(g => g.First())
            .Select(v => (Configuration: v, Stage: exam.StagesOf(v.Id!).Single())).ToList();
        Assert.Equal(exam.Stages.Count, pairs.Count);
        HarborCompile.Tasks(entryDir, exam, _ => new AgentRoute(null, null), pairs: pairs);

        var misses = new List<string>();
        foreach (var (assistant, c) in pairs)
        {
            var taskDir = Layout.HarborJobTask(entryDir, assistant.Id!, exam.Id);
            // Harbor's oracle agent runs a step's own solution/solve.sh.
            var solution = Path.Combine(taskDir, "steps", c.Id!, "solution");
            Directory.CreateDirectory(solution);
            foreach (var file in Directory.GetFiles(Path.Combine(exam.Dir, "reference"), $"{c.Id}.*")) File.Copy(file, Path.Combine(solution, Path.GetFileName(file)));
            File.WriteAllText(Path.Combine(solution, "solve.sh"), $"#!/usr/bin/env bash\nset -euo pipefail\nbash \"$(dirname \"$0\")/{c.Id}.sh\" {HarborCompile.Workdir}\n");

            var harborDir = Path.Combine(entryDir, "oracle-jobs", c.Id!);
            Directory.CreateDirectory(harborDir);
            var job = HarborCompile.Job(assistant, taskDir, Path.Combine(harborDir, "jobs"), new AgentRoute(null, null), engine: engine);
            job["agents"] = new JsonArray(new JsonObject { ["name"] = "oracle" });
            Runner.WriteJson(Path.Combine(harborDir, Layout.HarborJobFile), job);
            var run = Subprocess.Run(uv, HarborRun.UvArgs(exam.Def.Harbor!.Version), harborDir,
                new Dictionary<string, string> { ["HARBOR_TELEMETRY"] = "off", ["NO_COLOR"] = "1" }, timeout: TimeSpan.FromMinutes(30));

            var reward = Directory.GetFiles(Path.Combine(harborDir, "jobs"), Layout.RewardFile, SearchOption.AllDirectories).FirstOrDefault();
            if (reward is null) { misses.Add($"{c.Id}: no reward.json (Harbor exited {run.ExitCode}: {run.Stderr.Trim().Split('\n').LastOrDefault()})"); continue; }
            foreach (var (check, value) in JsonNode.Parse(File.ReadAllText(reward))!.AsObject())
                if (value?.GetValue<double>() != 1) misses.Add($"{c.Id}: {check} is {value?.ToJsonString()} with the reference solution");
        }
        Assert.True(misses.Count == 0, string.Join("\n", misses));
    }

    /// <summary>Every way a try differs from the stub's script, as "assistant/script: what"; empty when conformance is green.</summary>
    internal static List<string> Misses(string root, Exam exam)
    {
        var misses = new List<string>();
        var bank = Bank.Of(root, exam);
        foreach (var assistant in exam.Axes!.Def["assistant"].Values!.Keys)
        {
            var agent = exam.Axes.Value("assistant", assistant).Agent!;
            void Miss(string script, string what) => misses.Add($"{assistant}/{script}: {what}");
            // A try is one script's stage alone, in the configuration of that script and this assistant.
            string Attempt(string script) => bank.Single(a => a.Configuration == $"script={script},assistant={assistant}" && a.Stage == script).Dir(root);
            string Reason(string script) => Runner.ReadManifest(Path.Combine(Attempt(script), Layout.ManifestFile)).StatusReason ?? "";
            CountedCore? Core(string script) => File.Exists(Path.Combine(Attempt(script), Layout.CoreFile)) ? Runner.ReadJson<CountedCore>(Path.Combine(Attempt(script), Layout.CoreFile)) : null;

            if (Runner.ReadStatus(Attempt("ok")) is var ok && ok != AttemptStatus.Completed) Miss("ok", $"{ok}: {Reason("ok")}");
            else
            {
                var core = Core("ok")!;
                if (core.ToolCalls != 1 || core.Kinds.GetValueOrDefault("shell") != 1) Miss("ok", $"counted {core.ToolCalls} calls ({string.Join(", ", core.Kinds.Select(k => $"{k.Key}={k.Value}"))}), not one shell call");
                var reward = JsonNode.Parse(File.ReadAllText(Path.Combine(Attempt("ok"), Layout.RewardFile)))!;
                if (reward["made-file"]?.GetValue<double>() != 1) Miss("ok", $"the verifier's made-file is {reward["made-file"]?.ToJsonString() ?? "missing"}");
                var checks = Grade.ReadChecks(Attempt("ok"));
                if (checks is null) Miss("ok", "not graded");
                else if (Grade.Pass(checks, exam.Grading.Correctness!) != true)
                    Miss("ok", "does not pass: " + string.Join("; ", checks.Where(k => k.Value.Result != Verdict.Pass).Select(k => $"{k.Key}={k.Value.Result} ({k.Value.Reason})")));
                var answer = Transcript.Read(Attempt("ok")).Answer ?? "";
                if (!answer.StartsWith("tools: ")) Miss("ok", $"the answer is not the stub's tool list: '{answer}'");
                else
                {
                    var unmapped = answer["tools: ".Length..].Split(", ").Where(t => !Ingest.Kinds[agent].ContainsKey(t)).ToList();
                    if (unmapped.Count > 0) Miss("ok", $"tools the agent offers that Ingest.Kinds lacks: {string.Join(", ", unmapped)}");
                }
            }

            if (Runner.ReadStatus(Attempt("fail")) is var fail && fail != AttemptStatus.Failed) Miss("fail", $"{fail}, not failed: an outage must never read as a run");

            if (Runner.ReadStatus(Attempt("hang")) is var hang && hang != AttemptStatus.Completed) Miss("hang", $"{hang}: {Reason("hang")}");
            else if (Core("hang")?.ExitReason is var exit && exit != "time_budget") Miss("hang", $"exit reason {exit}, not time_budget");
        }
        return misses;
    }
}
