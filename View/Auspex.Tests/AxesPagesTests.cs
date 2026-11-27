using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Auspex.Tests;

/// <summary>
/// The pages of an exam with axes, over a bank proctor made: the exam, a configuration, a comparison and a try under
/// its own id. What a page says is held against what `proctor compare` and `proctor coverage` answer, since the pages
/// exist so that a number an agent quoted can be opened and found to be the same.
/// </summary>
public class AxesPagesTests(AxesBank bank) : IClassFixture<AxesBank>
{
    async Task<string> Get(string pathAndQuery, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var (app, url) = await Server.Start(bank.Root, 0);
        try
        {
            using var client = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = true });
            await client.GetAsync(url);
            var response = await client.GetAsync(new Uri(new Uri(url), pathAndQuery));
            Assert.Equal(expected, response.StatusCode);
            return await response.Content.ReadAsStringAsync();
        }
        finally { await app.StopAsync(); }
    }

    /// <summary>A page as its words, the markup dropped and runs of space made one.</summary>
    static string Text(string html) => Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")), @"\s+", " ");

    [Fact]
    public async Task TheExamPage_IsItsNamedConfigurationsAgainstLive_ItsPredictions_AndWhatTheExamIs()
    {
        var page = Text(await Get($"/{AxesBank.Exam}"));
        var html = await Get($"/{AxesBank.Exam}");
        // Each section is a word of the vocabulary, and every named configuration is a row, run or not.
        foreach (var heading in new[] { "Configurations", "Predictions", "Axes", "Stages", "Checks", "How this is counted" }) Assert.Contains(heading, page);
        foreach (var name in new[] { "shipped", "rewritten", "rewritten-b", "long-b" }) Assert.Contains($">{name}</a>", html);
        Assert.Contains("no tries yet", page);
        // The mark on a configuration is about its own tries against what is live: of proctor's answer, the one setting
        // that is this configuration, never the verdict pooled with other settings (another assistant's tries, here).
        var answer = JsonNode.Parse(bank.Proctor("compare", AxesBank.Exam, "--configuration", "rewritten"))!;
        Assert.True(answer["cells"]!.AsArray().Count(c => (string)c!["state"]! == "compared") > 1, "the bank compares this change in several settings");
        var own = answer["cells"]!.AsArray().Single(c => (string?)c!["value"]!["name"] == "rewritten")!;
        var held = own["held"]!;
        var mark = (string)held["direction"]! is "better" or "worse"
            ? $"{held["direction"]} vs shipped · {((bool)held["confirmed"]! ? "confirmed" : (bool)own["could_confirm"]! ? "not confirmed" : "cannot confirm")}"
            : "no difference vs shipped";
        Assert.Contains($">rewritten</a></span><wa-badge class=\"light {((bool)held["confirmed"]! ? $"light-{held["direction"]}" : "light-same")}\" appearance=\"accent\">{mark}</wa-badge>", html);
        Assert.Contains($"/{AxesBank.Exam}/compare?configuration=rewritten", html);
        // What the exam is: each check with its role, and how its stages run.
        Assert.Contains("correctness", page);
        Assert.Contains("The agent starts every stage with a fresh context.", page);
        // Any two configurations with tries can be ticked and compared; one with no try has no box.
        Assert.Contains("<wa-checkbox size=\"small\" data-pick=\"rewritten\"", html);
        Assert.DoesNotContain("data-pick=\"long-b\"", html);
        Assert.Contains($"<wa-button id=\"compare\" size=\"small\" appearance=\"outlined\" disabled data-to=\"/{AxesBank.Exam}/compare\">", html);
        // A read-only page: nothing on it submits anything, and the button only goes to another page.
        Assert.DoesNotContain("<form", html);
        Assert.DoesNotContain("<button", html);
    }

    [Fact]
    public async Task TheCoveragePage_IsTheConfigurationsSomebodyNamedOrRan_AndSaysWhatToAskAnAgent()
    {
        var page = Text(await Get($"/{AxesBank.Exam}/coverage"));
        // Proctor's counts: named and run, named and never run, and what is live across the assistant nobody chooses.
        var coverage = JsonNode.Parse(bank.Proctor("coverage", AxesBank.Exam))!;
        Assert.Equal(8, coverage["configurations"]!.AsArray().Count);
        Assert.Matches(@"rewritten-b prompt=rewritten .*?1 of 1", page);
        Assert.Matches(@"long-b length=long .*?not run 0 of 1", page);
        Assert.Contains("Live, across assistant", page);
        Assert.Contains("2 of 2 have tries", page);
        Assert.Contains("nobody runs every combination", page);
        // No finding of an agent's is on the page; it says what to ask one, and of what.
        Assert.Contains("This page shows what was measured, and no more.", page);
        Assert.Contains("proctor compare axes-answers prompt=rewritten", page);
    }

    [Fact]
    public async Task AComparisonsPage_ShowsTheNumbersProctorAnswers_AtTheAddressItGave()
    {
        var answer = JsonNode.Parse(bank.Proctor("compare", AxesBank.Exam, "prompt=rewritten"))!;
        var address = answer["address"]!.GetValue<string>();
        var html = await Get(address);
        var page = Text(html);

        Assert.Contains(Regex.Replace(answer["sentence"]!.GetValue<string>(), @"\s+", " "), page);
        Assert.Contains($"As it stood at {answer["as_of"]!.GetValue<string>()}", page);
        // Each try behind a number is on the page with what it held, linked to its own page.
        foreach (var cell in answer["cells"]!.AsArray().Where(c => c!["state"]!.GetValue<string>() == "compared"))
            foreach (var side in new[] { "value", "against" })
                foreach (var t in cell![side]!["tries"]!.AsArray())
                    Assert.Contains($"href=\"/{AxesBank.Exam}/try/{t!["id"]!.GetValue<string>()}\" title=\"{t["id"]!.GetValue<string>()}, made {t["made"]!.GetValue<string>()}\">{t["held"]}/{t["of"]}</a>", html);
        Assert.Contains("this many tries cannot confirm anything", page);

        // Pinned before any try was made, it shows that time's nothing, and what it is now beside it.
        var earlier = Text(await Get($"/{AxesBank.Exam}/compare?change=prompt%3Drewritten&against=prompt%3Dshipped&as_of=2020-01-01"));
        Assert.Contains("As it stood at 2020-01-01T23:59Z", earlier);
        Assert.Contains("4 tries have been made in these configurations since", earlier);
        Assert.Contains("nothing to compare yet", earlier);

        // A configuration is asked about by name, as `proctor compare --configuration` takes it.
        Assert.Contains("rewritten-b vs shipped-b", Text(await Get($"/{AxesBank.Exam}/compare?configuration=rewritten-b")));
        Assert.Contains("Asked of the configuration rewritten-b", Text(await Get($"/{AxesBank.Exam}/compare?configuration=rewritten-b&settings=all")));
        // What proctor refuses, the page says in proctor's words.
        Assert.Contains("is not a value this exam declares", Text(await Get($"/{AxesBank.Exam}/compare?change=prompt%3Dnope&against=prompt%3Dshipped")));
    }

    [Fact]
    public async Task ARetiredConfiguration_LeavesTheMainLists_AndKeepsItsPageAndItsReason()
    {
        bank.Proctor("retire", AxesBank.Exam, "long-b", "--reason", "nobody ships the long prompt on b");
        try
        {
            var exam = await Get($"/{AxesBank.Exam}");
            // Out of the rows, and under a count that opens the retired ones with their reasons.
            var (rows, retired) = (exam[..exam.IndexOf("<wa-details id=\"retired\"")], exam[exam.IndexOf("<wa-details id=\"retired\"")..]);
            Assert.DoesNotContain(">long-b</a>", rows);
            Assert.Contains(">rewritten</a>", rows);
            Assert.Contains("summary=\"1 retired\"", retired);
            Assert.Contains(">long-b</a>", retired);
            Assert.Contains("nobody ships the long prompt on b", retired);

            var index = await Get("/");
            Assert.DoesNotContain(">long-b</a>", index);
            Assert.Contains($"href=\"/{AxesBank.Exam}#retired\">1 retired</a>", index);

            var own = Text(await Get($"/{AxesBank.Exam}/configuration/long-b"));
            Assert.Contains("Retired", own);
            Assert.Contains("nobody ships the long prompt on b", own);
        }
        finally { bank.Proctor("retire", AxesBank.Exam, "long-b", "--bring-back"); }
        Assert.DoesNotContain("retired", await Get("/"));
    }

    [Fact]
    public async Task TheIndex_IsEachExam_WithItsNamedConfigurationsAndTheirMarks()
    {
        var html = await Get("/");
        var page = Text(html);
        Assert.Contains($"href=\"/{AxesBank.Exam}\"", html);
        // The same mark a configuration carries on its exam's page, which is proctor's verdict on it against what is live.
        foreach (var name in new[] { "shipped", "rewritten", "rewritten-b" }) Assert.Contains($">{name}</a>", html);
        var exam = await Get($"/{AxesBank.Exam}");
        var mark = Regex.Match(exam, "<wa-badge class=\"light [^\"]*\" appearance=\"accent\">[^<]*vs shipped[^<]*</wa-badge>");
        Assert.True(mark.Success, "the exam page marks a configuration against shipped");
        Assert.Contains(mark.Value, html);
        Assert.Contains("The one tried most recently is first.", page);
        Assert.DoesNotContain("<form", html);
    }

    [Fact]
    public async Task AComparisonInOneSetting_IsXVsY_TheSubjectFirst_WithProctorsNumbers()
    {
        var designed = 0;
        foreach (var (name, against) in new[] { ("rewritten", "shipped"), ("rewritten-b", "shipped-b") })
        {
            var answer = JsonNode.Parse(bank.Proctor("compare", AxesBank.Exam, "--configuration", name, "--against", against))!;
            var cells = answer["cells"]!.AsArray();
            // Proctor compares the change wherever the exam has it; the page is the setting that is these two configurations.
            var compared = cells.Where(c => c!["state"]!.GetValue<string>() == "compared" && c["value"]!["name"]?.GetValue<string>() == name).ToList();
            if (compared.Count != 1) continue;
            designed++;
            var html = await Get($"/{AxesBank.Exam}/compare?configuration={name}&against={against}");
            var page = Text(html);
            var other = compared[0]!["against"]!["name"]!.GetValue<string>();
            Assert.Contains($"{name} vs {other}", page);
            foreach (var heading in new[] { "Tries", "Checks", "Stages", "Friction", "How this is counted" }) Assert.Contains(heading, page);
            Assert.Contains($"Every number and direction describes {name}, the name before vs.", page);
            // Each check proctor compared is a row, with the rate proctor gives.
            foreach (var check in compared[0]!["by_check"]!.AsArray())
                Assert.Contains($"title=\"{check!["held"]} of {check["asked"]}\"", html);
            Assert.DoesNotContain("<form", html);
            // Every setting of the same change is one link away, with proctor's reading of them as one.
            if (cells.Count(c => c!["state"]!.GetValue<string>() == "compared") > 1)
                Assert.Contains("comparison · every setting", Text(await Get($"/{AxesBank.Exam}/compare?configuration={name}&against={against}&settings=all")));
        }
        Assert.True(designed > 0, "no comparison of the test bank is in one setting, so the designed page was not exercised");
    }

    [Fact]
    public async Task AConfigurationsPage_SaysWhatEachValueCarries_AndListsItsTries()
    {
        var key = "prompt=rewritten,tone=plain,examples=none,length=short,assistant=mock-b";
        var page = Text(await Get($"/{AxesBank.Exam}/configuration/{Uri.EscapeDataString(key)}"));
        Assert.Contains("rewritten-b", page);
        Assert.Contains("the guide rewritten: read note.txt first", page);
        Assert.Contains("files from values/prompt/rewritten", page);
        Assert.Contains("What a value changes is its author's account, not proctor's finding.", page);
        // The sections the canvas drew, each a word of the vocabulary, and each try's stages as squares that open a stage.
        foreach (var heading in new[] { "Predictions", "Stages", "Checks", "Friction", "Tries", "Axes" }) Assert.Contains(heading, page);
        var html = await Get($"/{AxesBank.Exam}/configuration/{Uri.EscapeDataString(key)}");
        Assert.Matches($"<a class=\"sq sq-(right|wrong)\" href=\"/{AxesBank.Exam}/try/{bank.Tries["rewritten-b"]}/", html);
        Assert.DoesNotContain("<form", html);
        Assert.Contains(bank.Tries["rewritten-b"], page);
        Assert.DoesNotContain(bank.Tries["rewritten"], page);

        var gone = Text(await Get($"/{AxesBank.Exam}/configuration/{Uri.EscapeDataString("prompt=gone,tone=plain")}"));
        Assert.Contains("Not a configuration this exam names or has tries of now.", gone);
    }

    [Fact]
    public async Task ATry_IsAddressedByItsOwnId_AndItsStageShowsEachVerdictByRole()
    {
        var id = bank.Tries["rewritten-b"];
        var stage = await Get($"/{AxesBank.Exam}/try/{id}");   // the try alone goes to its first stage
        Assert.Contains($"href=\"/{AxesBank.Exam}/configuration/", stage);

        // Every verdict of the stage is a row with its own address, under the role proctor graded it in, in proctor's words for the outcome.
        var page = Text(stage);
        Assert.Contains("correctness", page);
        Assert.Matches("id=\"check-[^\"]+\"", stage);
        Assert.Matches("did not hold|held", page);
        // What to read is an address, not a script: the prompt and the files are the same page asked another way.
        Assert.Contains($"/{AxesBank.Exam}/try/{id}/answer?show=prompt", stage);
        Assert.DoesNotContain("<form", stage);

        await Get($"/{AxesBank.Exam}/try/no-such-try", HttpStatusCode.NotFound);
    }
}
