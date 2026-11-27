using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;

namespace Auspex.Tests;

/// <summary>The real server, started on a free port, crawled from the index: every linked page and every named
/// asset answers 200, and every shipped asset is named by some page.</summary>
public class CrawlTests
{
    [Fact]
    public async Task EveryLinkedPageAndNamedAssetAnswers_AndEveryShippedAssetIsNamed()
    {
        using var bank = new SampleBank();
        var (app, url) = await Server.Start(bank.Root, 0);
        try
        {
            var crawl = await Crawl(url);
            Assert.Contains("/sample", crawl.Pages);
            Assert.Contains(SampleBank.Try, crawl.Pages);
            Assert.Contains("/sample/other-setup/task-one/1", crawl.Pages);
            Assert.Equal(Server.Assets.Select(a => "/" + a).Order(), crawl.Assets.Order());
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task TheExamPage_ListsTriesByLiveSetupFirst_AndCountsNothing()
    {
        using var bank = new SampleBank();
        var (app, url) = await Server.Start(bank.Root, 0);
        try
        {
            using var client = Client();
            await client.GetAsync(url);
            var page = await client.GetStringAsync(new Uri(new Uri(url), "/sample"));
            Assert.Contains("A made-up exam about the docs site", page);
            Assert.True(page.IndexOf("live-setup", StringComparison.Ordinal) < page.IndexOf("other-setup", StringComparison.Ordinal));
            Assert.Contains("live-setup <span class=\"meta\">live</span>", page);
            Assert.Contains("try 1</a> <span class=\"meta\">failed</span>", page);
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task NothingIsServedWithoutTheToken()
    {
        using var bank = new SampleBank();
        var (app, url) = await Server.Start(bank.Root, 0);
        try
        {
            using var client = new HttpClient();
            var origin = new Uri(url).GetLeftPart(UriPartial.Authority);
            foreach (var path in new[] { "/", "/sample", SampleBank.Try, "/assets/auspex.css", "/?t=wrong" })
                Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(origin + path)).StatusCode);
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task AStagePage_MarksTheQuoteInTheCallItCites_AndTagsTheStepsAPointCovers()
    {
        var page = await Page(SampleBank.Try);
        Assert.Contains("<mark>curl -s localhost:8081</mark>", page);
        Assert.Contains("<mark>Not\nfound</mark>", page);
        Assert.Contains("class=\"fric\" id=\"p1\" data-steps=\"2\"", page);
        Assert.Contains("<a class=\"tag\" href=\"#p1\"><span class=\"flabel\">fell-back</span></a>", page);
        Assert.Contains("class=\"step in-point\" id=\"s2\" open", page);
        // friction/2: a rule's point shows its `because`, its label's sentence on hover. A reader's point with no
        // `because` shows what the file holds, its notes.
        Assert.Contains("<span class=\"because\">used curl -s localhost:8081, a declared substitute, in the same step as the call to localhost:8080 failed (Not found)</span>", page);
        Assert.Contains("<span class=\"flabel\" title=\"a step where the agent used a substitute within two steps of a call to the surface that failed\">fell-back</span>", page);
        Assert.Contains("<span class=\"because\">tries again</span>", page);
    }

    [Fact]
    public async Task AFailedCheck_OpensFailureFirst_WithItsWhyAndItsStepsLit()
    {
        var page = await Page(SampleBank.Try);
        Assert.True(page.IndexOf("class=\"card sel\" data-check=\"acceptance\"", StringComparison.Ordinal) < page.IndexOf("class=\"friction\"", StringComparison.Ordinal));
        Assert.Contains("data-steps=\"3\"", page);
        Assert.Contains("<span class=\"lbl\">Why it is checked</span><p>the task is only done when its tests pass</p>", page);
        Assert.Contains("<a href=\"#s3\">3</a>", page);
        Assert.Contains("script <code>exams/sample/checks/acceptance.sh</code>", page);
        Assert.Contains("<span class=\"lbl\">Files</span> <code>README.md</code>", page);
        Assert.Contains("class=\"step lit\" id=\"s3\" open", page);
        Assert.Contains("class=\"gm gm-fail\"", page);
        Assert.Contains("title=\"the acceptance tests pass\">acceptance</code>", page);
        Assert.DoesNotContain("Show the checks", page);
        Assert.Contains("<span class=\"flagged\">1 flagged step</span>", page);
    }

    [Fact]
    public async Task AGreenStage_OpensOnTheTranscript_WithTheChecksOneClickAway()
    {
        var page = await Page(SampleBank.GreenTry);
        Assert.Contains("class=\"panes quiet\"", page);
        Assert.Contains("<wa-details summary=\"Show the checks\"", page);
        Assert.DoesNotContain("class=\"card", page);
        Assert.Contains("No <code>friction.json</code>", page);
    }

    [Fact]
    public async Task AStage_IsAddressedBySetupTryAndStage_WithTheTrysStagesInOrder()
    {
        var page = await Page(SampleBank.Stage);
        // The strip: every stage of the try by its id, in the order they ran, the one shown marked.
        Assert.Matches("<nav class=\"stages\"[^>]*><a href=\"/staged/live-setup/1/write-note\" aria-current=\"page\">.*write-note</a><a href=\"/staged/live-setup/1/read-note\" class=\"unfinished\">.*read-note <span class=\"meta\">skipped</span></a></nav>", page);
        Assert.Contains("the attempt ended with &#39;max_tool_calls&#39;", page);

        // A try alone opens on its first stage.
        Assert.Contains("<h1><code>write-note</code></h1>", await Page(SampleBank.StagedTry));
    }

    [Fact]
    public async Task AnOraclesAnswer_IsShownAsTheOracles_WithTheSheetEntryItUsed_AndAnUnansweredEndingIsSaid()
    {
        using var bank = new SampleBank();
        var (app, url) = await Server.Start(bank.Root, 0);
        try
        {
            using var client = Client();
            await client.GetAsync(url);
            var page = await client.GetStringAsync(new Uri(new Uri(url), SampleBank.Stage));
            // The answer is the oracle's, never shown as a turn the person typed; the first prompt stays the user's.
            Assert.Contains("<b>3</b> <span class=\"meta oracle\">oracle · answered from <code>which-file</code></span>", page);
            Assert.Contains("<b>1</b> <span class=\"meta\">user</span>", page);
            Assert.Contains("1 answer from the oracle", page);
            // A question the sheet does not cover leaves no turn, so the ending is said in the transcript.
            Assert.Contains("<b>The oracle had no answer</b> to the agent's last message", page);
            Assert.Contains("(oracle_miss)", page);
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task AStageNotReached_SaysProctorsReason_AndAsksForNothingThatCannotExist()
    {
        var page = await Page(SampleBank.StageNotReached);
        Assert.Contains("<b>skipped</b> · not reached: stage write-note was stopped (max_tool_calls)", page);
        Assert.DoesNotContain("proctor grade</code> writes it", page);
    }

    [Fact]
    public async Task TheExamPage_ListsAStagedTryWithItsStages()
    {
        var page = await Page("/staged");
        Assert.Contains("<a href=\"/staged/live-setup/1\">try 1</a> <a href=\"/staged/live-setup/1/write-note\">write-note</a> · <a href=\"/staged/live-setup/1/read-note\">read-note</a> <span class=\"meta\">skipped</span>", page);
    }

    static async Task<string> Page(string path)
    {
        using var bank = new SampleBank();
        var (app, url) = await Server.Start(bank.Root, 0);
        try
        {
            using var client = Client();
            await client.GetAsync(url);
            return await client.GetStringAsync(new Uri(new Uri(url), path));
        }
        finally { await app.StopAsync(); }
    }

    /// <summary>Opt in with AUSPEX_TEST_PUBLISH=1: builds what a user installs and repeats the crawl against it.</summary>
    [Fact]
    public async Task ThePublishedProgram_PassesTheSameCrawl()
    {
        if (Environment.GetEnvironmentVariable("AUSPEX_TEST_PUBLISH") != "1") return;
        using var bank = new SampleBank();
        var output = Path.Combine(bank.Root, "..", "published");
        var project = Path.Combine(RepoRoot(), "Auspex.csproj");
        using (var publish = Process.Start(new ProcessStartInfo("dotnet", ["publish", project, "-c", "Release", "-o", output, "--nologo", "-v", "q"]) { RedirectStandardOutput = true })!)
        {
            await publish.WaitForExitAsync();
            Assert.Equal(0, publish.ExitCode);
        }

        // Run from another directory: nothing may be found relative to where the program was built or started.
        using var served = Process.Start(new ProcessStartInfo("dotnet", [Path.Combine(output, "auspex.dll"), bank.Root]) { RedirectStandardOutput = true, WorkingDirectory = Path.GetTempPath() })!;
        try
        {
            var url = await served.StandardOutput.ReadLineAsync();
            Assert.NotNull(url);
            var crawl = await Crawl(url);
            Assert.Contains(SampleBank.Try, crawl.Pages);
            Assert.Contains("/assets/auspex.css", crawl.Assets);
        }
        finally { served.Kill(); }
    }

    /// <summary>Opt in with AUSPEX_TEST_ROOT=dir: every page over a real proctor root's bank renders.</summary>
    [Fact]
    public async Task ARealProctorRoot_RendersEveryPage()
    {
        if (Environment.GetEnvironmentVariable("AUSPEX_TEST_ROOT") is not { Length: > 0 } root) return;
        var (app, url) = await Server.Start(root, 0);
        try { Assert.NotEmpty((await Crawl(url)).Pages); }
        finally { await app.StopAsync(); }
    }

    /// <summary>
    /// Every page linked from the index, every asset a page names, and everything those assets import: a script's
    /// relative `import` and `export ... from`, a stylesheet's `@import`. A module a page can reach is a module this
    /// fetched, so a component chunk can't hide behind another chunk (project/build-plan.md, step 2). A link to a file
    /// under /assets/ (the licence) counts as an asset, not a page.
    /// </summary>
    static async Task<(HashSet<string> Pages, HashSet<string> Assets)> Crawl(string url)
    {
        using var client = Client();
        var origin = new Uri(new Uri(url).GetLeftPart(UriPartial.Authority));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(url)).StatusCode);

        var pages = new HashSet<string>();
        var assets = new HashSet<string>();
        var queue = new Queue<string>(["/"]);
        var pending = new Queue<string>();
        void Asset(string path) { if (assets.Add(path)) pending.Enqueue(path); }

        while (queue.TryDequeue(out var path))
        {
            if (!pages.Add(path)) continue;
            var response = await client.GetAsync(new Uri(origin, path));
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} answered {(int)response.StatusCode}");
            var html = await response.Content.ReadAsStringAsync();
            foreach (Match link in Regex.Matches(html, "<a(?: [^>]*)? href=\"(/[^\"#]*)"))
            {
                var target = WebUtility.HtmlDecode(link.Groups[1].Value);
                if (target.StartsWith("/assets/", StringComparison.Ordinal)) Asset(target); else queue.Enqueue(target);
            }
            foreach (Match asset in Regex.Matches(html, "(?:<link[^>]* href|<script[^>]* src|<img[^>]* src)=\"(/[^\"]*)\""))
                Asset(WebUtility.HtmlDecode(asset.Groups[1].Value));
        }

        while (pending.TryDequeue(out var asset))
        {
            var response = await client.GetAsync(new Uri(origin, asset));
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{asset} answered {(int)response.StatusCode}");
            var body = await response.Content.ReadAsByteArrayAsync();
            Assert.NotEmpty(body);
            var imports = asset.EndsWith(".js", StringComparison.Ordinal) ? JsImport.Matches(System.Text.Encoding.UTF8.GetString(body))
                : asset.EndsWith(".css", StringComparison.Ordinal) ? CssImport.Matches(System.Text.Encoding.UTF8.GetString(body)) : null;
            foreach (Match m in imports ?? (IEnumerable<Match>)[])
            {
                var target = m.Groups.Values.Skip(1).First(g => g.Success).Value;
                Assert.False(target.StartsWith("http", StringComparison.Ordinal), $"{asset} imports from the network: {target}");
                Asset(new Uri(new Uri(origin, asset), target).AbsolutePath);
            }
        }
        return (pages, assets);
    }

    static readonly Regex JsImport = new("""(?:\bimport|\bexport)\s*(?:[^'"]*?\bfrom\s*)?["'](\.[^"']+)["']|\bimport\(\s*["'](\.[^"']+)["']\s*\)""");
    // A stylesheet's imports, and the files it names by url(): its fonts.
    static readonly Regex CssImport = new("""@import\s+['"]([^'"]+)['"]|url\(\s*['"]?([^'")]+)['"]?\s*\)""");

    static HttpClient Client() => new(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = true });

    static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "Auspex.csproj"))) dir = Path.GetDirectoryName(dir) ?? throw new InvalidOperationException("Auspex.csproj not found above the tests");
        return dir;
    }
}
