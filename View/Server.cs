using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace Auspex;

/// <summary>The one server: loopback only, every page behind a per-start token, every asset out of the assembly
/// through one handler. There is no development mode and nothing is read relative to the working directory.</summary>
public static class Server
{
    public const string AssetPrefix = "assets/";

    public static IReadOnlyList<string> Assets { get; } =
        [.. typeof(Server).Assembly.GetManifestResourceNames().Where(n => n.StartsWith(AssetPrefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal)];

    /// <summary>Starts on the port (0 picks a free one) and returns the app with the URL to open, token included.</summary>
    public static async Task<(WebApplication App, string Url)> Start(string root, int port)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, port));
        var app = builder.Build();

        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        const string cookie = "auspex";

        // An attempt can hold keys in its config snapshots, so nothing is served without the token the start printed.
        app.Use(async (context, next) =>
        {
            if (context.Request.Query["t"] == token)
            {
                context.Response.Cookies.Append(cookie, token, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/" });
                context.Response.Redirect(context.Request.Path);
                return;
            }
            if (context.Request.Cookies[cookie] != token)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Open the URL auspex printed when it started; it carries this start's token.");
                return;
            }
            await next();
        });

        app.MapGet("/", () => Html(Pages.Index(root)));
        app.MapGet("/assets/{**name}", (string name) =>
            typeof(Server).Assembly.GetManifestResourceStream(AssetPrefix + name) is { } stream ? Results.Stream(stream, ContentType(name)) : Results.NotFound());
        // An exam, then a try of it. On an exam of stages: setup, try, stage; a try alone goes to its first stage. On an
        // exam of tasks, the bank's own path: setup, task, number.
        app.MapGet("/{exam}", (string exam) =>
            Bank.FindExam(root, exam) is { } e ? Html(e.Axes ? ExamPage.Render(root, e) : Pages.Exam(e)) : Results.NotFound());
        // An exam with axes (proctor's plans/axes-build.md, 3d): the addresses `proctor compare` and `proctor coverage` give in their
        // answers. A try sits under its own id; a comparison and coverage carry the time they were answered (as_of).
        app.MapGet("/{exam}/coverage", (string exam, HttpRequest request) =>
            Bank.FindExam(root, exam) is { Axes: true } e ? Html(AxesPages.Exam(root, e, request.Query["as_of"])) : Results.NotFound());
        app.MapGet("/{exam}/compare", (string exam, HttpRequest request) =>
            Bank.FindExam(root, exam) is { Axes: true } e ? Html(AxesPages.Compare(root, e, key => request.Query[key])) : Results.NotFound());
        app.MapGet("/{exam}/configuration/{key}", (string exam, string key) =>
            Bank.FindExam(root, exam) is { Axes: true } e ? Html(AxesPages.Configuration(root, e, key)) : Results.NotFound());
        app.MapGet("/{exam}/try/{id}", (string exam, string id) =>
            Bank.FindTryById(root, exam, id) is { Count: > 0 } stages ? Results.Redirect(Pages.U(stages[0].Path)) : Results.NotFound());
        app.MapGet("/{exam}/try/{id}/{stage}", (string exam, string id, string stage, HttpRequest request) =>
            Bank.FindTryById(root, exam, id) is { Count: > 0 } stages && stages.FirstOrDefault(s => s.Task == stage) is { } a
                ? Html(StagePage.Render(a, stages, request.Query["check"])) : Results.NotFound());
        app.MapGet("/{exam}/{variant}/{n}", (string exam, string variant, string n) =>
            Bank.FindTry(root, exam, variant, n) is { Count: > 0 } stages ? Results.Redirect(Pages.U(stages[0].Path)) : Results.NotFound());
        app.MapGet("/{exam}/{variant}/{first}/{second}", (string exam, string variant, string first, string second, HttpRequest request) =>
            Bank.FindAttempt(root, exam, variant, first, second) is { } a ? Html(StagePage.Render(a, Bank.FindTry(root, exam, variant, a.N), request.Query["check"])) : Results.NotFound());

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return (app, $"{address}/?t={token}");
    }

    static IResult Html(string page) => Results.Content(page, "text/html; charset=utf-8");

    static string ContentType(string name) => Path.GetExtension(name) switch
    {
        ".css" => "text/css; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".woff2" => "font/woff2",
        ".md" => "text/plain; charset=utf-8",
        _ => "application/octet-stream",
    };
}
