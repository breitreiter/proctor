using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;

namespace Auspex.View.View.Tests;

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
    public async Task AnExamProctorCannotRead_IsListedByWhatItsBankHolds_AndCountsNothing()
    {
        using var bank = new SampleBank();
        var (app, url) = await Server.Start(bank.Root, 0);
        try
        {
            using var client = Client();
            await client.GetAsync(url);
            var page = await client.GetStringAsync(new Uri(new Uri(url), "/sample"));
            Assert.Contains("A made-up exam about the docs site", page);
            Assert.Contains("Proctor cannot read this exam as it stands, so nothing is counted.", page);
            Assert.True(page.IndexOf("live-setup", StringComparison.Ordinal) < page.IndexOf("other-setup", StringComparison.Ordinal));
            Assert.Contains("<span class=\"name\">live-setup</span></span>", page);
            Assert.DoesNotContain("light-live", page);
            Assert.Contains("try 1</a> <wa-badge class=\"tag tag-none\" appearance=\"outlined\">failed</wa-badge>", page);
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
            foreach (var path in new[] { "/", "/sample", SampleBank.Try, "/assets/one.css", "/?t=wrong" })
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
        // A friction point names its label, with the label's sentence on hover, and the step it covers as a pill; the step carries the label.
        Assert.Matches("id=\"p1\">.*?<wa-badge class=\"tag tag-fr\" appearance=\"outlined\" id=\"tip\\d+\" tabindex=\"0\">fell-back</wa-badge><wa-tooltip for=\"tip\\d+\">a step where the agent used a substitute within two steps of a call to the surface that failed</wa-tooltip><span class=\"pills\"><wa-button class=\"pill\" size=\"small\" pill appearance=\"outlined\" href=\"#s2\"", page);
        Assert.Matches("<wa-details class=\"step step-fr\" id=\"s2\" appearance=\"plain\">.*?<wa-badge class=\"tag tag-fr\" appearance=\"outlined\">fell-back</wa-badge>", page);
        // friction/2: a rule's point shows its `because`. A reader's point with no `because` shows what the file holds, its notes.
        Assert.Contains("<span class=\"small soft\">used curl -s localhost:8081, a declared substitute, in the same step as the call to localhost:8080 failed (Not found)</span>", page);
        Assert.Contains("<span class=\"small soft\">tries again</span>", page);
        // Flagged steps start folded; one button opens them together.
        Assert.Contains("<wa-button id=\"open-flagged\"", page);
        Assert.DoesNotContain("<wa-button id=\"open-flagged\"", await Page(SampleBank.GreenTry));
    }

    [Fact]
    public async Task ACheckThatDidNotHold_SaysWhyItIsChecked_AndPointsAtItsStepsAndFiles()
    {
        var page = await Page(SampleBank.Try);
        Assert.Matches("id=\"check-acceptance\"><span class=\"wrong strong\">did not hold</span>", page);
        Assert.Contains("title=\"the acceptance tests pass\">acceptance</span>", page);
        Assert.Contains("Why it is checked: the task is only done when its tests pass", page);
        Assert.Contains("<span class=\"small dim mono\">exams/sample/checks/acceptance.sh</span>", page);
        Assert.Contains("appearance=\"outlined\" href=\"#s3\"", page);
        Assert.Contains("<span class=\"small dim mono\">README.md</span>", page);
        Assert.Contains("1 flagged step", page);
        // Graded before proctor recorded roles: the page says so, and guesses none.
        Assert.Contains("role not recorded", page);
    }

    [Fact]
    public async Task AStageWithNoFriction_ListsItsChecks_AndSaysFrictionWasNotRead()
    {
        var page = await Page(SampleBank.GreenTry);
        Assert.Contains(">held</span>", page);
        Assert.DoesNotContain("did not hold", page);
        Assert.Contains("Friction has not been read on this stage.", page);
    }

    [Fact]
    public async Task AStage_IsAddressedBySetupTryAndStage_WithTheTrysStagesInOrder()
    {
        var page = await Page(SampleBank.Stage);
        // The tabs: every stage of the try by its id, in the order they ran, the one shown selected; one that did not complete says its status.
        Assert.Matches("<a class=\"tab stab tab-on\"[^>]*href=\"/staged/live-setup/1/write-note\">.*?write-note</span></a><a class=\"tab stab\"[^>]*href=\"/staged/live-setup/1/read-note\">.*?read-note</span><wa-badge class=\"tag tag-none\" appearance=\"outlined\">skipped</wa-badge></a>", page);
        Assert.Contains("the attempt ended with &#39;max_tool_calls&#39;", page);

        // A try alone opens on its first stage.
        Assert.Contains("<h1 class=\"title\">write-note</h1>", await Page(SampleBank.StagedTry));
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
            Assert.Matches("id=\"s3\"[^>]*>.*?<span class=\"who\">oracle &#183; answered from which-file</span>", page);
            Assert.Matches("id=\"s1\"[^>]*>.*?<span class=\"who\">user</span>", page);
            Assert.Contains("<dt>Oracle</dt><dd>1 answer</dd>", page);
            // A question the sheet does not cover leaves no turn, so the ending is said in the transcript.
            Assert.Contains("<b>The oracle had no answer</b>", page);
            Assert.Contains(">oracle_miss</span>", page);
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task AStageNotReached_SaysProctorsReason_AndAsksForNothingThatCannotExist()
    {
        var page = await Page(SampleBank.StageNotReached);
        Assert.Contains("<b>skipped</b> <span class=\"soft\">not reached: stage write-note was stopped (max_tool_calls)", page);
        Assert.DoesNotContain("Grading the exam writes it", page);
    }

    [Fact]
    public async Task WhatTheBankHolds_ListsAStagedTryWithItsStages()
    {
        var page = await Page("/staged");
        Assert.Contains("<a href=\"/staged/live-setup/1\">try 1</a><span class=\"small\"><a class=\"mono\" href=\"/staged/live-setup/1/write-note\">write-note</a></span><span class=\"small\"><a class=\"mono\" href=\"/staged/live-setup/1/read-note\">read-note</a> <wa-badge class=\"tag tag-none\" appearance=\"outlined\">skipped</wa-badge></span>", page);
    }

    [Fact]
    public async Task EarlierAttempts_StayReachable_AndAreSaidToBeInNoNumber()
    {
        var page = await Page("/staged/earlier");
        Assert.Contains("<span class=\"kind\">earlier attempts</span>", page);
        Assert.Contains("Made by an earlier proctor, whose files this one does not read.", page);
        Assert.Contains("href=\"/staged/live-setup/1/write-note\"", page);
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
        var project = Path.Combine(RepoRoot(), "Auspex.View.csproj");
        using (var publish = Process.Start(new ProcessStartInfo("dotnet", ["publish", project, "-c", "Release", "-o", output, "--nologo", "-v", "q"]) { RedirectStandardOutput = true })!)
        {
            await publish.WaitForExitAsync();
            Assert.Equal(0, publish.ExitCode);
        }

        // Run from another directory: nothing may be found relative to where the program was built or started.
        using var served = Process.Start(new ProcessStartInfo("dotnet", [Path.Combine(output, "auspex-view.dll"), bank.Root]) { RedirectStandardOutput = true, WorkingDirectory = Path.GetTempPath() })!;
        try
        {
            var url = await served.StandardOutput.ReadLineAsync();
            Assert.NotNull(url);
            var crawl = await Crawl(url);
            Assert.Contains(SampleBank.Try, crawl.Pages);
            Assert.Contains("/assets/one.css", crawl.Assets);
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
        while (!File.Exists(Path.Combine(dir, "Auspex.View.csproj"))) dir = Path.GetDirectoryName(dir) ?? throw new InvalidOperationException("Auspex.csproj not found above the tests");
        return dir;
    }
}
