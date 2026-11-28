using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

/// <summary>
/// A configuration through Harbor (plans/axes-build.md, slice 4), as it is compiled: its values' files in the image's
/// repository, its text parameters as build arguments, a tree parameter beside the fixture's services, the stages that
/// apply as the job's steps, and the verifier told which configuration it is. Nothing here starts a container; the
/// scripted control (Proctor.Tests/exams/axes-harbor) was run in Docker by hand when this was built.
/// </summary>
public class AxesHarborTests
{
    static (TestRepo Repo, Exam Exam) Control()
    {
        var repo = new TestRepo();
        repo.CopyExam("axes-harbor");
        return (repo, repo.LoadExam("axes-harbor"));
    }

    static string Compile(TestRepo repo, Exam exam, string key)
    {
        var dir = Path.Combine(repo.Root, "compiled", key);
        HarborCompile.Job(dir, exam, exam.Variants.Single(v => v.Id == key), new AgentRoute(null, null), nbImage: "proctor-nb:test");
        return dir;
    }

    [Fact]
    public void AConfiguration_IsCompiledIntoItsImage_ItsParametersAsBuildArguments_ItsFilesInTheRepository()
    {
        var (repo, exam) = Control();
        using var _ = repo;
        // Who does the work comes from a value: nb, through Harbor because the value says so.
        Assert.All(exam.Variants, v => Assert.True(v is { IsAgent: true, IsVendor: false, Agent: "nb", Provider: "Mock" }));

        var old = Compile(repo, exam, "toolchain=1.20,docs=rewritten,assistant=nb-mock");
        var dockerfile = File.ReadAllText(Path.Combine(old, "environment", "Dockerfile"));
        // A build argument, set before the image's own step reads it, and never the agent's environment: a parameter can say what is planted.
        Assert.Contains("ARG TOOLCHAIN=\"1.20\"\n", dockerfile);
        Assert.True(dockerfile.IndexOf("ARG TOOLCHAIN", StringComparison.Ordinal) < dockerfile.IndexOf("RUN echo \"toolchain $TOOLCHAIN\"", StringComparison.Ordinal));
        Assert.DoesNotContain("PROCTOR_PARAM", dockerfile);
        Assert.DoesNotContain("ENV TOOLCHAIN", dockerfile);
        Assert.Contains("audit --json", File.ReadAllText(Path.Combine(old, "environment", "repo", "README.md")));

        // The steps are the stages that apply in this configuration; the other toolchain has both.
        Assert.Equal(["read"], Directory.GetDirectories(Path.Combine(old, "steps")).Select(Path.GetFileName));
        var current = Compile(repo, exam, "toolchain=1.23,docs=shipped,assistant=nb-mock");
        Assert.Equal(["modern-only", "read"], Directory.GetDirectories(Path.Combine(current, "steps")).Select(Path.GetFileName).Order());
        Assert.Contains("ARG TOOLCHAIN=\"1.23\"\n", File.ReadAllText(Path.Combine(current, "environment", "Dockerfile")));
        Assert.DoesNotContain("audit --json", File.ReadAllText(Path.Combine(current, "environment", "repo", "README.md")));

        // The verifier's checks are told the configuration, as a check on the bare path is.
        var env = File.ReadAllText(Path.Combine(old, "steps", "read", "tests", "env.sh"));
        Assert.Contains("export PROCTOR_AXIS_TOOLCHAIN='1.20'", env);
        Assert.Contains("export PROCTOR_AXIS_DOCS='rewritten'", env);
        Assert.Contains("export PROCTOR_PARAM_TOOLCHAIN='1.20'", env);
    }

    [Fact]
    public void ATreeParameter_SitsBesideTheServices_UnderItsName_AndIsEmptyWhenNoValueSetsIt()
    {
        using var repo = new TestRepo();
        repo.CopyExam("tern-docs");
        var exam = repo.LoadExam("tern-docs");
        Assert.Equal(["docs=as-shipped,assistant=sonnet", "docs=restructure,assistant=sonnet", "docs=none,assistant=sonnet"], exam.Variants.Select(v => v.Id));

        var shipped = Compile(repo, exam, "docs=as-shipped,assistant=sonnet");
        Assert.True(File.Exists(Path.Combine(shipped, "environment", "params", "DOCS", "quickstart.html")));
        Assert.Contains("./params/DOCS:/srv/docs:ro", File.ReadAllText(Path.Combine(shipped, "environment", "docker-compose.yaml")));
        // A tree is files for a service, never a build argument; and the old bundle directory is not made for a configuration.
        Assert.DoesNotContain("ARG DOCS", File.ReadAllText(Path.Combine(shipped, "environment", "Dockerfile")));
        Assert.False(Directory.Exists(Path.Combine(shipped, "environment", "bundle")));

        var none = Compile(repo, exam, "docs=none,assistant=sonnet");
        Assert.True(Directory.Exists(Path.Combine(none, "environment", "params", "DOCS")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(none, "environment", "params", "DOCS")));
    }

    [Fact]
    public void Build_ReadsWhatAHarborConfigurationsImageBuildIsHanded()
    {
        var (repo, exam) = Control();
        using var _ = repo;
        var result = Build.Run(repo.Root, exam, Exam.LoadConfig(repo.Root, []), null, null, null, TextWriter.Null);
        Assert.Empty(result.Noise);

        var toolchain = result.Values.Single(v => v.Axis == "toolchain");
        Assert.Equal(["Dockerfile"], toolchain.Paths.Select(p => p.Path));
        Assert.Contains("+ARG TOOLCHAIN=\"1.20\"", toolchain.Diff);
        // It says what it compared, and that the image was not built.
        Assert.Contains("the image is not built", toolchain.Compared);
        Assert.Equal(["repo/README.md"], result.Values.Single(v => v.Axis == "docs").Paths.Select(p => p.Path));
        // The named configuration of both is their sum: nothing reads the two together.
        Assert.Equal(("rewritten-old", 0, 0), (result.Combinations.Single().Name, result.Combinations.Single().Differs.Count, result.Combinations.Single().Unclear.Count));
    }

    [Fact]
    public void ATryOfOneStageWithAnAnswerSheet_IsCompiledAsAPlainTask_AndOneOfSeveralIsRefused()
    {
        using var repo = new TestRepo();
        repo.CopyExam("gate-oracle");
        var exam = repo.LoadExam("gate-oracle");
        var harbor = exam.Variants.Where(v => v.IsAgent).ToList();
        Assert.Equal(3, harbor.Count);
        Assert.All(harbor, v => Assert.True(HarborCompile.Plain(exam, v.Id!)));
        Assert.All(exam.Variants.Where(v => !v.IsAgent), v => Assert.Single(exam.StagesOf(v.Id!)));

        // The simulated user answers a task, never a step: the try is a task, with its instruction and its verifier at the top.
        var entry = Path.Combine(repo.Root, "entry");
        HarborCompile.Tasks(entry, exam, _ => new AgentRoute(null, null), nbImage: "proctor-nb:test", pairs: harbor.Select(v => (v, exam.StagesOf(v.Id!)[0])).ToList());
        var task = Layout.HarborJobTask(entry, "ending=ok,route=harbor", exam.Id);
        Assert.Contains("MOCK:oracle=deploy-target", File.ReadAllText(Path.Combine(task, "instruction.md")));
        Assert.True(File.Exists(Path.Combine(task, "tests", "test.sh")));
        Assert.False(Directory.Exists(Path.Combine(task, "steps")));
        Assert.True(File.Exists(Path.Combine(HarborCompile.AgentsDir(entry), "oracle_user.py")));

        // A stage with no condition would join every try, making each two stages with an answer sheet among them: that is refused, and says what to do.
        repo.EditJson("gate-oracle/exam.json", e => e["stages"]!.AsArray().Add(JsonNode.Parse("""{ "id": "after", "prompt": "MOCK:response=done" }""")));
        Assert.Contains(repo.Problems("gate-oracle"), p => p.Field == "axes" && p.Message.Contains("runs through Harbor a try of 2 stages") && p.Message.Contains("Give it a try of one stage"));
    }

    [Fact]
    public void AnExamWhoseAgentIsAValue_NeedsHarborPinned_EvenWhenNoConfigurationIsNamed()
    {
        var (repo, _) = Control();
        using var __ = repo;
        // With nothing named no configuration is carried, yet a value still says Harbor: the block is not "nothing runs through Harbor".
        repo.EditJson("axes-harbor/exam.json", e => e.Remove("configurations"));
        Assert.Empty(repo.Problems("axes-harbor"));
        repo.EditJson("axes-harbor/exam.json", e => e.Remove("harbor"));
        Assert.Contains(repo.Problems("axes-harbor"), p => p.Field == "harbor.version" && p.Message.Contains("required where a value runs through Harbor"));
    }
}
