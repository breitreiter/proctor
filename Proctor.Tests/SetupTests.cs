using System.Net;
using Proctor;

namespace Proctor.Tests;

public class SetupTests
{
    /// <summary>Answers by URL path from a table, and keeps every request so a test can check where it went and how it authenticated.</summary>
    sealed class RoutedHttp(params (string PathEnd, HttpStatusCode Status, string Body)[] routes) : HttpMessageHandler
    {
        public readonly List<HttpRequestMessage> Requests = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            var route = routes.FirstOrDefault(r => request.RequestUri!.AbsolutePath.EndsWith(r.PathEnd));
            return Task.FromResult(route.PathEnd is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{"error":{"message":"Not Found"}}""") }
                : new HttpResponseMessage(route.Status) { Content = new StringContent(route.Body) });
        }

        public string? Header(int i, string name) =>
            Requests[i].Headers.TryGetValues(name, out var values) ? values.Single() : null;
    }

    sealed class Unreachable : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("Connection refused (localhost:11434)");
    }

    static string Captured(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "models", name + ".json"));

    static Task<KeyCheckResult> Check(string kind, string? key, HttpMessageHandler http, string? baseUrl = null) =>
        KeyCheck.RunAsync(Catalogue.Get(kind), key, baseUrl, new HttpClient(http));

    // ── the catalogue ──

    [Fact]
    public void Catalogue_LoadsEveryKind_WithAHelpAnchorOnTheSite_AndNoVendorUrl()
    {
        Assert.Equal(["gemini", "anthropic", "openai", "openrouter", "openai-compatible", "azure-openai", "azure-foundry"],
            Catalogue.Kinds.Select(k => k.Kind));
        foreach (var kind in Catalogue.Kinds)
        {
            Assert.Equal($"https://breitreiter.github.io/proctor/providers.html#{kind.Kind}", kind.HelpUrl);
            Assert.False(string.IsNullOrWhiteSpace(kind.Where), kind.Kind);
            Assert.Contains(kind.Fields, f => f.Name == "ApiKey" && f.Secret);
            Assert.All(kind.Serves.Concat(kind.ByHand ?? []), job => Assert.Contains(job.TrimEnd('?'), Catalogue.Jobs));
        }
    }

    [Fact]
    public void Catalogue_NamesEverySecretsVariable_ExceptTheKindTheAgentCompletes()
    {
        foreach (var field in Catalogue.Kinds.Where(k => !k.CompletedByAgent).SelectMany(k => k.Fields).Where(f => f.Secret))
            Assert.Matches("^[A-Z][A-Z0-9_]*_API_KEY$", field.Env);
        Assert.Null(Catalogue.Get("openai-compatible").Fields.Single().Env);
    }

    [Fact]
    public void Catalogue_AsksForAModel_OnlyWhereTheUserProvisionedOne()
    {
        var withModel = Catalogue.Kinds.Where(k => k.Fields.Any(f => f.Name is "Model" or "ChatDeploymentName")).Select(k => k.Kind);
        Assert.Equal(["azure-openai", "azure-foundry"], withModel);
    }

    [Theory]
    [InlineData("anthropic", "claude-code", "Yes")]
    [InlineData("openai", "claude-code", "No")]
    [InlineData("openrouter", "claude-code", "Untested")]
    [InlineData("openrouter", "codex", "ByHand")]
    [InlineData("openrouter", "classifier", "Yes")]
    [InlineData("gemini", "judge", "Untested")]
    public void Fit_ReadsServesAndByHand(string kind, string job, string expected) =>
        Assert.Equal(expected, Catalogue.Get(kind).Fit(job).ToString());

    [Fact]
    public void Get_AnUnknownKind_NamesTheKnownOnes() =>
        Assert.Contains("known: gemini, anthropic", Assert.Throws<ProctorException>(() => Catalogue.Get("bedrock")).Message);

    // ── the key check, against captured answers ──

    [Fact]
    public async Task Gemini_ListsTheModelsThatGenerate_WithTheKeyInItsHeader()
    {
        var http = new RoutedHttp(("/v1beta/models", HttpStatusCode.OK, Captured("gemini")));
        var result = await Check("gemini", "AIza-test-key", http);

        Assert.Equal(KeyCheckStatus.Ok, result.Status);
        Assert.Equal(["gemini-2.5-flash", "gemini-flash-lite-latest"], result.Models);   // the embedding model is dropped
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models?pageSize=1000", http.Requests[0].RequestUri!.ToString());
        Assert.Equal("AIza-test-key", http.Header(0, "x-goog-api-key"));
        Assert.DoesNotContain("key=", http.Requests[0].RequestUri!.Query);   // never in the URL, where logs keep it
    }

    [Fact]
    public async Task Anthropic_ListsModels_WithItsKeyHeaderAndVersion()
    {
        var http = new RoutedHttp(("/v1/models", HttpStatusCode.OK, Captured("anthropic")));
        var result = await Check("anthropic", "sk-ant-test", http);

        Assert.Equal(["claude-haiku-5-5", "claude-opus-5-5", "claude-sonnet-5-5"], result.Models);
        Assert.Equal("sk-ant-test", http.Header(0, "x-api-key"));
        Assert.Equal("2023-06-01", http.Header(0, "anthropic-version"));
    }

    [Fact]
    public async Task OpenAI_ListsModels_WithABearerKey()
    {
        var http = new RoutedHttp(("/v1/models", HttpStatusCode.OK, Captured("openai")));
        var result = await Check("openai", "sk-test", http);

        Assert.Equal(["gpt-5-mini", "gpt-5-mini-2025-08-07", "tts-1"], result.Models);
        Assert.Equal("Bearer sk-test", http.Header(0, "Authorization"));
    }

    [Fact]
    public async Task OpenRouter_ProvesTheKeyOnItsOwnEndpoint_ThenLists()
    {
        var http = new RoutedHttp(("/api/v1/key", HttpStatusCode.OK, Captured("openrouter-key")), ("/api/v1/models", HttpStatusCode.OK, Captured("openrouter-models")));
        var result = await Check("openrouter", "sk-or-test", http);

        Assert.Equal(KeyCheckStatus.Ok, result.Status);
        Assert.Contains("liquid/lfm-3-1.7b:free", result.Models);
        Assert.Equal(["/api/v1/key", "/api/v1/models"], http.Requests.Select(r => r.RequestUri!.AbsolutePath));
    }

    [Fact]
    public async Task OpenRouter_ARefusedKey_StopsBeforeTheList()
    {
        var http = new RoutedHttp(("/api/v1/key", HttpStatusCode.Unauthorized, """{"error":{"message":"No auth credentials found","code":401}}"""));
        var result = await Check("openrouter", "sk-or-bad", http);

        Assert.Equal(KeyCheckStatus.Failed, result.Status);
        Assert.Equal("401 No auth credentials found", result.Words);
        Assert.Equal("the key was refused", result.Reading);
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task OpenAICompatible_WithoutAnEndpoint_IsNotAvailable_AndSendsNothing()
    {
        var http = new RoutedHttp();
        var result = await Check("openai-compatible", null, http);

        Assert.Equal(KeyCheckStatus.NotAvailable, result.Status);
        Assert.Contains("coding agent", result.Reading);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task OpenAICompatible_ListsFromTheEntrysEndpoint_WithNoKeyHeaderWhenThereIsNoKey()
    {
        var http = new RoutedHttp(("/v1/models", HttpStatusCode.OK, """{"object":"list","data":[{"id":"qwen3:8b"},{"id":"gemma3:4b"}]}"""));
        var result = await Check("openai-compatible", null, http, "http://localhost:11434/v1/");

        Assert.Equal(["gemma3:4b", "qwen3:8b"], result.Models);
        Assert.Equal("http://localhost:11434/v1/models", http.Requests[0].RequestUri!.ToString());
        Assert.Null(http.Header(0, "Authorization"));
    }

    [Theory]
    [InlineData("azure-openai")]
    [InlineData("azure-foundry")]
    public async Task Azure_HasNoKeyCheck(string kind)
    {
        var http = new RoutedHttp();
        Assert.Equal(KeyCheckStatus.NotAvailable, (await Check(kind, "key", http)).Status);
        Assert.Empty(http.Requests);
    }

    // ── how a failure reads ──

    [Fact]
    public async Task Gemini_ABadKeyIs400_AndReadsAsRefused()
    {
        var http = new RoutedHttp(("/v1beta/models", HttpStatusCode.BadRequest,
            """{"error":{"code":400,"message":"API key not valid. Please pass a valid API key.","status":"INVALID_ARGUMENT"}}"""));
        var result = await Check("gemini", "AIza-bad-key", http);

        Assert.Equal("400 API key not valid. Please pass a valid API key.", result.Words);
        Assert.Equal("the key was refused", result.Reading);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "", "the key was refused")]
    [InlineData(HttpStatusCode.NotFound, "", "this endpoint has no model list; is the endpoint right?")]
    [InlineData(HttpStatusCode.TooManyRequests, "", "rate-limited or out of quota; the key itself may be fine")]
    [InlineData(HttpStatusCode.BadGateway, "", "the provider had an error; try again later")]
    [InlineData(HttpStatusCode.BadRequest, "malformed", "the provider answered 400")]
    public void Reading_IsTheSameForEveryKind(HttpStatusCode status, string body, string expected) =>
        Assert.Equal(expected, KeyCheck.Reading(status, body));

    [Fact]
    public async Task AnUnreachableEndpoint_NamesItsHost()
    {
        var result = await Check("openai-compatible", null, new Unreachable(), "http://localhost:11434/v1");
        Assert.Equal(KeyCheckStatus.Failed, result.Status);
        Assert.Equal("could not reach localhost; is the endpoint right?", result.Reading);
    }

    [Fact]
    public async Task AKeyQuotedInAnError_IsRedacted()
    {
        var http = new RoutedHttp(("/v1/models", HttpStatusCode.Unauthorized, """{"error":{"message":"Incorrect API key provided: sk-proj-abcdef123456"}}"""));
        var result = await Check("openai", "sk-proj-abcdef123456", http);
        Assert.Equal("401 Incorrect API key provided: ****", result.Words);
    }

    [Fact]
    public async Task ASuccessThatIsNotAModelList_SaysTheEndpointIsWrong()
    {
        var http = new RoutedHttp(("/v1/models", HttpStatusCode.OK, "<html>login</html>"));
        var result = await Check("openai-compatible", null, http, "http://proxy.example/v1");
        Assert.Equal("the answer was not JSON; is the endpoint right?", result.Reading);
    }
}
