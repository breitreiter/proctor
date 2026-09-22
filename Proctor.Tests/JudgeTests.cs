using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Proctor;

namespace Proctor.Tests;

/// <summary>The two model checks through fakes: windows, decide over systemone, judge over chat, the verdict file as cache, validation, and the grade-to-report path.</summary>
public class JudgeTests
{
    // ---- fakes ----

    /// <summary>A chat model that answers from a script, one answer per call, and counts the calls.</summary>
    sealed class ScriptedChat(params string[] answers) : IChatClient
    {
        readonly Queue<string> _answers = new(answers);
        public int Calls;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            var text = _answers.Count > 0 ? _answers.Dequeue() : answers[^1];
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)) { Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 20 } });
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    /// <summary>A systemone endpoint that returns one body, and counts the calls.</summary>
    sealed class ScriptedHttp(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls;
        public string? LastBody;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }

    static readonly JudgeDef Jev = new(JudgeDef.SystemOne, "http://judge.test/systemone", null, null, "typesafe", null);
    static readonly JudgeDef Glm = new(JudgeDef.Chat, "http://judge.test/v1", "glm", null, "glm", null);

    static JudgeClient Client(ScriptedHttp? http = null, ScriptedChat? chat = null, bool rejudge = false) => new()
    {
        Defs = new() { ["jev"] = Jev, ["glm"] = Glm },
        Http = new HttpClient(http ?? new ScriptedHttp(HttpStatusCode.InternalServerError, "unused")),
        ChatClient = (_, _) => chat ?? throw new InvalidOperationException("no chat fake"),
        Rejudge = rejudge,
    };

    static Transcript Fixture(string name, string? diff = null) =>
        Transcript.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name + ".jsonl")), diff);

    static CellContext Cell(JudgeClient judges, JsonObject? expect = null, string? cellDir = null) =>
        new("/nonexistent", cellDir ?? Directory.CreateTempSubdirectory("proctor-judge").FullName, "/work", "exp", "a", new CaseDef("c", null, "Say the answer.", expect), 1) { Judges = judges };

    static Verdict Eval(string spec, Transcript t, JudgeClient judges, JsonObject? expect = null, string? cellDir = null, string name = "the-check") =>
        Checks.Evaluate(JsonNode.Parse(spec)!.AsObject(), Cell(judges, expect, cellDir), t, name);

    static string Answer(string label, params string[] evidence) =>
        $"<thinking>because</thinking>\n```json\n{new JsonObject { ["label"] = label, ["evidence"] = new JsonArray(evidence.Select(e => (JsonNode)e).ToArray()) }.ToJsonString()}\n```";

    const string Noul = "{\"state\":\"Completed\",\"result\":{\"model\":\"jev-1.13.0\",\"answers\":{\"q\":{\"type\":\"noul\",\"noul\":P}},\"usage\":{\"input_tokens\":287,\"output_tokens\":21}}}";
    const string Choice = "{\"result\":{\"model\":\"jev-1.13.0\",\"answers\":{\"q\":{\"type\":\"choice\",\"choice\":\"complete\",\"probabilities\":{\"complete\":0.97,\"asked\":0.02,\"blocked\":0.01}}}}}";

    // ---- windows ----

    [Fact]
    public void Window_ExtractsNamedPartsAndRefusesEmptyOrOversized()
    {
        var cell = Cell(Client());
        var (parts, _) = Window.Extract("answer+prompt", cell, Fixture("plain"), 24_000);
        Assert.Equal(["answer", "prompt"], parts!.Keys);
        Assert.Equal("The answer is forty-two.", parts["answer"]);
        Assert.Contains("### answer", Window.Compile(parts));
        Assert.Contains("<prompt>\nSay the answer.\n</prompt>", Window.Compile(parts));

        var (calls, _) = Window.Extract("tool_calls", cell, Fixture("tool-allowed"), 24_000);
        Assert.StartsWith("1. bash {\"command\":\"echo hi\"", calls!["tool_calls"]);

        var (none, error) = Window.Extract("diff", cell, Fixture("plain"), 24_000);
        Assert.Null(none);
        Assert.Equal("window 'diff' is empty", error);

        var (big, tooBig) = Window.Extract("answer", cell, Fixture("plain"), 10);
        Assert.Null(big);
        Assert.StartsWith("window too large", tooBig);

        Assert.Null(Window.Validate("answer+diff"));
        Assert.StartsWith("unknown window transcript", Window.Validate("answer+transcript"));
    }

    // ---- decide ----

    [Theory]
    [InlineData(0.98, "pass", "yes p=0.98")]
    [InlineData(0.04, "fail", "no p=0.96")]
    [InlineData(0.70, "needs-judge", "yes p=0.70 (below 0.90)")]
    public void Decide_ReadsANoulProbabilityAgainstTheThreshold(double p, string result, string reason)
    {
        var http = new ScriptedHttp(HttpStatusCode.OK, Noul.Replace("P", p.ToString("0.00")));
        var v = Eval("{\"decide\": {\"ask\": \"Is it done?\", \"window\": \"answer\"}}", Fixture("plain"), Client(http));
        Assert.Equal((result, reason), (v.Result, v.Reason));
        var sent = JsonNode.Parse(http.LastBody!)!;
        Assert.Equal("noul", sent["questions"]!["q"]!["type"]!.GetValue<string>());
        Assert.Equal("The answer is forty-two.", sent["state"]!["answer"]!["content"]!.GetValue<string>());
        Assert.Equal(Window.Known["answer"], sent["state"]!["answer"]!["what_this_is"]!.GetValue<string>());
    }

    [Fact]
    public void Decide_ChoiceSendsCriteriaAndComparesTheChosenLabel()
    {
        var http = new ScriptedHttp(HttpStatusCode.OK, Choice);
        const string spec = "{\"decide\": {\"ask\": \"What does it claim?\", \"window\": \"answer\", \"options\": {\"complete\": \"done\", \"asked\": \"a question\", \"blocked\": \"stuck\"}, \"expect\": \"EXPECT\", \"threshold\": 0.95}}";
        Assert.Equal(("pass", "complete p=0.97"), Eval(spec.Replace("EXPECT", "complete"), Fixture("plain"), Client(http)).Deconstruct());
        Assert.Equal(("fail", "complete p=0.97"), Eval(spec.Replace("EXPECT", "asked"), Fixture("plain"), Client(http)).Deconstruct());
        Assert.Equal("done", JsonNode.Parse(http.LastBody!)!["questions"]!["q"]!["criteria"]!["complete"]!.GetValue<string>());

        // expect from the case, keyed by the check's name
        var fromCase = Eval(spec.Replace("\"EXPECT\"", "\"@expect\""), Fixture("plain"), Client(http), new JsonObject { ["stance"] = "complete" }, name: "stance");
        Assert.Equal("pass", fromCase.Result);
        var missing = Eval(spec.Replace("\"EXPECT\"", "\"@expect\""), Fixture("plain"), Client(http), new JsonObject(), name: "stance");
        Assert.Equal(("error", "decide: case has no expect.stance"), missing.Deconstruct());
    }

    [Fact]
    public void Decide_EndpointFailuresAreErrorsNeverFails()
    {
        var down = Eval("{\"decide\": {\"ask\": \"?\", \"window\": \"answer\"}}", Fixture("plain"), Client(new ScriptedHttp(HttpStatusCode.BadGateway, "upstream gone")));
        Assert.Equal("error", down.Result);
        Assert.Contains("HTTP 502", down.Reason);

        var odd = Eval("{\"decide\": {\"ask\": \"?\", \"window\": \"answer\"}}", Fixture("plain"), Client(new ScriptedHttp(HttpStatusCode.OK, "{\"result\": {\"answers\": {}}}")));
        Assert.Equal(("error", "decide: jev returned no noul answer"), odd.Deconstruct());

        var empty = Eval("{\"decide\": {\"ask\": \"?\", \"window\": \"diff\"}}", Fixture("plain"), Client(new ScriptedHttp(HttpStatusCode.OK, Noul)));
        Assert.Equal(("error", "decide: window 'diff' is empty"), empty.Deconstruct());
    }

    // ---- judge ----

    [Fact]
    public void Judge_UnanimousSamplesPassOrFailWithTheFirstQuote()
    {
        var yes = new ScriptedChat(Answer("yes", "forty-two"));
        var v = Eval("{\"judge\": {\"ask\": \"Does it answer?\", \"window\": \"answer\"}}", Fixture("plain"), Client(chat: yes));
        Assert.Equal(("pass", "yes 3/3 — \"forty-two\""), v.Deconstruct());
        Assert.Equal(3, yes.Calls);

        var no = new ScriptedChat(Answer("no", "The answer is"));
        Assert.Equal(("fail", "no 3/3 — \"The answer is\""), Eval("{\"judge\": {\"ask\": \"?\", \"window\": \"answer\"}}", Fixture("plain"), Client(chat: no)).Deconstruct());

        var expectNo = Eval("{\"judge\": {\"ask\": \"?\", \"window\": \"answer\", \"expect\": \"no\", \"samples\": 1}}", Fixture("plain"), Client(chat: new ScriptedChat(Answer("no", "forty-two"))));
        Assert.Equal(("pass", "no 1/1 — \"forty-two\""), expectNo.Deconstruct());
    }

    [Fact]
    public void Judge_SplitOrUnknownIsNeedsJudge()
    {
        var split = new ScriptedChat(Answer("yes", "forty-two"), Answer("no", "forty-two"), Answer("yes", "forty-two"));
        var v = Eval("{\"judge\": {\"ask\": \"?\", \"window\": \"answer\"}}", Fixture("plain"), Client(chat: split));
        Assert.Equal(("needs-judge", "split: yes 2, no 1 of 3 — \"forty-two\""), v.Deconstruct());

        var unknown = new ScriptedChat(Answer("yes", "forty-two"), Answer("unknown"), Answer("yes", "forty-two"));
        Assert.StartsWith("unknown: yes 2, unknown 1 of 3", Eval("{\"judge\": {\"ask\": \"?\", \"window\": \"answer\"}}", Fixture("plain"), Client(chat: unknown)).Reason);
    }

    [Fact]
    public void Judge_DiscardsUnverifiableOrUnparsableSamples_AndErrorsWhenTooFewRemain()
    {
        // one bad quote among three: the two verified samples decide
        var oneBad = new ScriptedChat(Answer("yes", "forty-two"), Answer("yes", "forty-three"), Answer("yes", "The  answer is\nforty-two"));
        var v = Eval("{\"judge\": {\"ask\": \"?\", \"window\": \"answer\"}}", Fixture("plain"), Client(chat: oneBad));
        Assert.Equal(("pass", "yes 2/3 — \"forty-two\""), v.Deconstruct());

        // no json, no evidence, a bad quote: nothing usable
        var allBad = new ScriptedChat("I think yes.", Answer("yes"), Answer("no", "nowhere"));
        var e = Eval("{\"judge\": {\"ask\": \"?\", \"window\": \"answer\"}}", Fixture("plain"), Client(chat: allBad));
        Assert.Equal("error", e.Result);
        Assert.Contains("0 of 3 usable answers from glm", e.Reason);
        Assert.Contains("no json block", e.Reason);
        Assert.Contains("no evidence quoted", e.Reason);
        Assert.Contains("not found verbatim", e.Reason);
    }

    // ---- the verdict file ----

    [Fact]
    public void VerdictFile_IsWrittenBesideTheEvidence_AndReusedUntilRejudge()
    {
        var dir = Directory.CreateTempSubdirectory("proctor-judge").FullName;
        var http = new ScriptedHttp(HttpStatusCode.OK, Noul.Replace("P", "0.98"));
        const string spec = "{\"decide\": {\"ask\": \"Is it done?\", \"window\": \"answer\"}}";

        Assert.Equal("pass", Eval(spec, Fixture("plain"), Client(http), cellDir: dir, name: "done").Result);
        var files = Directory.GetFiles(Path.Combine(dir, Layout.VerdictsDir));
        var file = Assert.Single(files);
        Assert.Matches(@"done\.jev\.[0-9a-f]{8}\.json$", file);
        var written = JsonSerializer.Deserialize<VerdictFile>(File.ReadAllText(file), Proctor.Eval.JsonOptions)!;
        Assert.Equal(("done", "jev", "systemone", "jev-1.13.0", "answer"), (written.Check, written.Judge, written.Kind, written.Model, written.Window));
        Assert.Equal(287, written.Usage!.Input);
        Assert.Equal(0.98, written.Samples[0].Probability);
        Assert.Equal("pass", written.Verdict.Result);
        Assert.Contains("forty-two", written.Request.ToJsonString());

        Assert.Equal("pass", Eval(spec, Fixture("plain"), Client(http), cellDir: dir, name: "done").Result);
        Assert.Equal(1, http.Calls);   // the file answered

        Assert.Equal("pass", Eval(spec.Replace("Is it done?", "Is it finished?"), Fixture("plain"), Client(http), cellDir: dir, name: "done").Result);
        Assert.Equal(2, http.Calls);   // a different question is a different file
        Assert.Equal(2, Directory.GetFiles(Path.Combine(dir, Layout.VerdictsDir)).Length);

        Assert.Equal("pass", Eval(spec, Fixture("plain"), Client(http, rejudge: true), cellDir: dir, name: "done").Result);
        Assert.Equal(3, http.Calls);

        // an error on file is retried, not reused
        var down = new ScriptedHttp(HttpStatusCode.BadGateway, "gone");
        Assert.Equal("error", Eval(spec.Replace("done?", "over?"), Fixture("plain"), Client(down), cellDir: dir, name: "done").Result);
        Assert.Equal("error", Eval(spec.Replace("done?", "over?"), Fixture("plain"), Client(down), cellDir: dir, name: "done").Result);
        Assert.Equal(2, down.Calls);
    }

    [Fact]
    public void Remap_GradesWithTheOtherJudge_AndKeepsItsOwnFile()
    {
        var dir = Directory.CreateTempSubdirectory("proctor-judge").FullName;
        var http = new ScriptedHttp(HttpStatusCode.OK, Noul.Replace("P", "0.98"));
        var client = new JudgeClient
        {
            Defs = new() { ["jev"] = Jev, ["jev-local"] = Jev with { Endpoint = "http://imp:8083/v1/systemone" } },
            Http = new HttpClient(http), Remap = new() { ["jev"] = "jev-local" },
        };
        Eval("{\"decide\": {\"ask\": \"?\", \"window\": \"answer\", \"with\": \"jev\"}}", Fixture("plain"), client, cellDir: dir, name: "done");
        Assert.Contains("done.jev-local.", Assert.Single(Directory.GetFiles(Path.Combine(dir, Layout.VerdictsDir))));
    }

    // ---- validation ----

    [Theory]
    [InlineData("{\"decide\": \"x\"}", "expects {ask, window")]
    [InlineData("{\"decide\": {\"window\": \"answer\"}}", "ask is required")]
    [InlineData("{\"decide\": {\"ask\": \"?\", \"window\": \"everything\"}}", "unknown window everything")]
    [InlineData("{\"decide\": {\"ask\": \"?\", \"window\": \"answer\", \"options\": [\"a\", \"b\"]}}", "expect is required: one of a, b")]
    [InlineData("{\"decide\": {\"ask\": \"?\", \"window\": \"answer\", \"options\": [\"a\"], \"expect\": \"a\"}}", "at least two labels")]
    [InlineData("{\"decide\": {\"ask\": \"?\", \"window\": \"answer\", \"threshold\": 1.5}}", "threshold is a probability")]
    [InlineData("{\"decide\": {\"ask\": \"?\", \"window\": \"answer\", \"expect\": \"maybe\"}}", "expect is true or false")]
    [InlineData("{\"decide\": {\"ask\": \"?\", \"window\": \"answer\", \"model\": \"x\"}}", "unknown field 'model'")]
    [InlineData("{\"judge\": {\"ask\": \"?\", \"window\": \"answer\", \"samples\": 0}}", "samples is a whole number")]
    [InlineData("{\"judge\": {\"ask\": \"?\", \"window\": \"answer\", \"expect\": \"perhaps\"}}", "expect is yes or no")]
    [InlineData("{\"not_judge\": {\"ask\": \"?\", \"window\": \"answer\"}}", "cannot be negated")]
    public void Validation_ReportsTheShape(string spec, string problem)
    {
        var problems = Checks.ValidateCheck(JsonNode.Parse(spec)!.AsObject(), "/nonexistent").ToList();
        Assert.Contains(problems, p => p.Problem.Contains(problem));
    }

    [Fact]
    public void Validation_PassesAWellFormedCheckAndDescribesIt()
    {
        var decide = JsonNode.Parse("{\"decide\": {\"ask\": \"What does it claim?\", \"window\": \"answer\", \"options\": [\"complete\", \"asked\"], \"expect\": \"complete\"}}")!.AsObject();
        Assert.Empty(Checks.ValidateCheck(decide, "/nonexistent"));
        Assert.Equal("a decider answers 'complete' to: What does it claim?", Checks.Describe(decide));
        var judge = JsonNode.Parse("{\"judge\": {\"ask\": \"Is the change in scope?\", \"window\": \"prompt+diff\"}}")!.AsObject();
        Assert.Empty(Checks.ValidateCheck(judge, "/nonexistent"));
        Assert.Equal("a judge says yes to: Is the change in scope?", Checks.Describe(judge));
        Assert.True(Checks.AsksAModel(judge));
        Assert.False(Checks.AsksAModel(JsonNode.Parse("{\"exit_reason\": \"ok\"}")!.AsObject()));
    }

    [Fact]
    public void Config_ValidatesTheJudgesBlock_AndTheEvalResolvesItsJudges()
    {
        using var repo = new TestRepo();
        repo.CopyEval("smoke");
        repo.WriteProctorConfig(new
        {
            jev = new { kind = "systemone", endpoint = "http://judge.test/systemone", model = "should-not-be-here" },
            glm = new { kind = "chat", endpoint = "not a url" },
            odd = new { kind = "oracle", endpoint = "http://x/" },
        });
        var problems = new List<Problem>();
        Proctor.Eval.LoadConfig(repo.Root, problems);
        Assert.Contains(problems, p => p.Field == "judges.jev.model" && p.Message.Contains("endpoint's"));
        Assert.Contains(problems, p => p.Field == "judges.glm.model" && p.Message.Contains("required"));
        Assert.Contains(problems, p => p.Field == "judges.glm.endpoint");
        Assert.Contains(problems, p => p.Field == "judges.odd.kind" && p.Message.Contains("unknown kind"));

        // a decide with no systemone judge configured, and a judge naming one that is not there
        repo.WriteProctorConfig(new { glm = new { kind = "chat", endpoint = "http://judge.test/v1", model = "glm" } });
        repo.EditJson("smoke/eval.json", e =>
        {
            e["grading"]!["checks"]!["stance"] = JsonNode.Parse("{\"decide\": {\"ask\": \"?\", \"window\": \"answer\"}}");
            e["grading"]!["checks"]!["scope"] = JsonNode.Parse("{\"judge\": {\"ask\": \"?\", \"window\": \"answer\", \"with\": \"k2\"}}");
        });
        problems.Clear();
        var config = Proctor.Eval.LoadConfig(repo.Root, problems);
        Assert.Empty(problems);
        Assert.Null(Proctor.Eval.Load(repo.Root, "smoke", problems, config.Judges));
        Assert.Contains(problems, p => p.Field == "grading.checks.stance.decide.with" && p.Message == "no systemone judge in evals/proctor.json judges");
        Assert.Contains(problems, p => p.Field == "grading.checks.scope.judge.with" && p.Message == "no judge 'k2' in evals/proctor.json judges");
        Assert.NotNull(repo.LoadEval("smoke"));   // without the config in hand, the eval loads; grade resolves
        Assert.True(repo.LoadEval("smoke").Judged);
    }

    // ---- grade to report ----

    [Fact]
    public void Grade_WritesModelVerdictsIntoChecksJson_AndTheReportNamesTheJudge()
    {
        using var repo = new TestRepo();
        repo.CopyEval("smoke");
        repo.EditJson("smoke/eval.json", e =>
        {
            e["arms"]!.AsArray().RemoveAt(1); e["arms"]![0]!["samples"] = 1; e["arms"]![0]!["model"] = "glm-4.7";
            e["grading"]!["checks"]!["stance"] = JsonNode.Parse("{\"decide\": {\"ask\": \"Does it claim completion?\", \"window\": \"answer\"}}");
            e["grading"]!["checks"]!["sensible"] = JsonNode.Parse("{\"judge\": {\"ask\": \"Is the answer sensible?\", \"window\": \"prompt+answer\", \"samples\": 2, \"with\": \"glm\"}}");
            e["grading"]!["pass"] = JsonNode.Parse("[\"exit_ok\", \"sensible\"]");
        });
        repo.WriteProctorConfig(new { jev = new { kind = "systemone", endpoint = "http://judge.test/systemone", family = "typesafe" }, glm = new { kind = "chat", endpoint = "http://judge.test/v1", model = "glm", family = "glm" } });
        var problems = new List<Problem>();
        var config = Proctor.Eval.LoadConfig(repo.Root, problems);
        var eval = repo.LoadEval("smoke");
        var id = Runner.Start(repo.Root, eval, config, null, null, "test", TextWriter.Null);
        var experiment = Runner.LoadExperiment(repo.Root, id);

        var http = new ScriptedHttp(HttpStatusCode.OK, Noul.Replace("P", "0.99"));
        var chat = new ScriptedChat(Answer("yes", "OK"));
        var judges = new JudgeClient { Defs = JudgeClient.From(config, null, rejudge: false).Defs, Http = new HttpClient(http), ChatClient = (_, _) => chat };
        var log = new StringWriter();
        var grades = Proctor.Grade.Experiment(repo.Root, experiment, eval, log, judges);

        Assert.Contains("arm 'a' (glm-4.7) is in the family of judge 'glm' (glm)", log.ToString());
        var plain = grades.Single(g => g.Case == "plain");
        Assert.Equal("pass", plain.Checks!["stance"].Result);
        Assert.Equal("yes p=0.99", plain.Checks["stance"].Reason);
        Assert.Equal("error", plain.Checks["sensible"].Result);   // "OK" is not in plain's answer: every sample discarded
        var usesBash = grades.Single(g => g.Case == "uses-bash");
        Assert.Equal(("pass", "yes 2/2 — \"OK\""), usesBash.Checks!["sensible"].Deconstruct());
        Assert.True(usesBash.Pass);
        Assert.Equal(3, http.Calls);
        Assert.Equal(6, chat.Calls);

        var cell = Layout.Cell(Layout.Experiment(repo.Root, id), "a", "plain", 1);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(cell, Layout.VerdictsDir)).Length);

        // a comparison pass: k2 beside glm, checks.json untouched, k2's files not applied
        repo.WriteProctorConfig(new { jev = new { kind = "systemone", endpoint = "http://judge.test/systemone" }, glm = new { kind = "chat", endpoint = "http://judge.test/v1", model = "glm" }, k2 = new { kind = "chat", endpoint = "http://judge.test/v1", model = "k2" } });
        var k2 = new ScriptedChat(Answer("no", "OK"));
        var compare = new JudgeClient { Defs = JudgeClient.From(Proctor.Eval.LoadConfig(repo.Root, problems), ["glm=k2"], false).Defs, Remap = new() { ["glm"] = "k2" }, Http = new HttpClient(http), ChatClient = (def, _) => def.Model == "k2" ? k2 : chat };
        var compareLog = new StringWriter();
        var compared = Proctor.Grade.Experiment(repo.Root, experiment, eval, compareLog, compare);
        Assert.Equal(("pass", "yes 2/2 — \"OK\""), compared.Single(g => g.Case == "uses-bash").Checks!["sensible"].Deconstruct());
        Assert.Equal("pass", Proctor.Grade.ReadChecks(Layout.Cell(Layout.Experiment(repo.Root, id), "a", "uses-bash", 1))!["sensible"].Result);
        Assert.Contains("a/uses-bash/1  sensible: glm=pass (yes 2/2 — \"OK\")  k2=fail (no 2/2 — \"OK\")", compareLog.ToString());
        Assert.Contains("2 of 3 verdicts agree; checks.json unchanged", compareLog.ToString());   // plain and loops error under both judges ("OK" is not in their answers); uses-bash disagrees
        Assert.Equal(3, http.Calls);   // decides are not remapped, so not re-called
        Assert.Equal(6, chat.Calls);
        Assert.Equal(6, k2.Calls);
        var k2File = Directory.GetFiles(Path.Combine(Layout.Cell(Layout.Experiment(repo.Root, id), "a", "uses-bash", 1), Layout.VerdictsDir), "sensible.k2.*").Single();
        Assert.False(JsonSerializer.Deserialize<VerdictFile>(File.ReadAllText(k2File), Proctor.Eval.JsonOptions)!.Applied);

        var uses = Judge.Uses(repo.Root, experiment, eval);
        Assert.Equal(["glm", "jev"], uses.Select(u => u.Judge));
        Assert.Equal(["sensible"], uses[0].Checks);
        Assert.Equal(("systemone", "jev-1.13.0"), (uses[1].Kind, uses[1].Model));

        var rows = Results.Collect(repo.Root, experiment, eval);
        var stats = Stats.Compute(experiment, eval, rows);
        var html = Report.Html(stats, rows, experiment, uses);
        Assert.Contains("<dt>judge jev</dt><dd><span class=\"id\">systemone jev-1.13.0 at http://judge.test/systemone; graded stance</span></dd>", html);
        Assert.Contains("| judge glm | `chat glm at http://judge.test/v1; graded sensible` |", Report.Markdown(stats, rows, experiment, uses));
    }
}

static class VerdictExtensions
{
    public static (string, string) Deconstruct(this Verdict v) => (v.Result, v.Reason);
}
