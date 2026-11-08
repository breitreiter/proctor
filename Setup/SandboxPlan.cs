using System.Runtime.InteropServices;
using System.Text;

namespace Proctor;

/// <summary>
/// <c>suites/sandbox-plan.md</c>: a brief for the user's coding agent, written when the sandbox is to run on this machine.
/// It holds what setup could see and what proctor needs, never a fixed list of commands: the route depends on the OS,
/// the distro, licences and what a work laptop allows, which takes a strong model talking to the user to resolve. The
/// mockup and its reasoning are <c>project/plans/setup/sandbox-plan.md</c>.
/// </summary>
static class SandboxPlan
{
    /// <summary>One observed fact: what was asked, what came back. Nothing here is a diagnosis.</summary>
    public record Observation(string What, string Found);

    /// <summary>The engine to name in proctor.json from what is installed: docker when it is there, else podman when it is, else none.</summary>
    public static string? EngineFrom(IReadOnlyList<Observation> seen) =>
        Found(seen, "docker") ? "docker" : Found(seen, "podman") ? "podman" : null;

    /// <summary>Asks the machine. Each probe is one short command; a missing tool is "not found", never an error.</summary>
    public static List<Observation> Observe()
    {
        var seen = new List<Observation>
        {
            new("OS", $"{RuntimeInformation.OSDescription.Trim()}, {RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}"),
            new("uv", Version("uv", "--version")),
            new("docker", Version("docker", "--version")),
        };
        if (Found(seen, "docker")) seen.Add(new("docker compose", Version("docker", "compose", "version")));
        seen.Add(new("podman", Version("podman", "--version")));
        if (Found(seen, "podman"))
        {
            seen.Add(new("`podman compose`", Version("podman", "compose", "version")));
            if (OperatingSystem.IsLinux())
                seen.Add(new("`podman.socket` (user)", Version("systemctl", "--user", "is-active", "podman.socket")));
            seen.Add(new("podman search registries", Version("podman", "info", "--format", "{{json .Registries.search}}")));
        }
        if (OperatingSystem.IsWindows()) seen.Add(new("WSL", Version("wsl", "--status")));
        return seen;
    }

    public static string Render(DateOnly today, IReadOnlyList<Observation> seen, IEnumerable<string> wantedAgents, string? engine)
    {
        var wanted = wantedAgents.ToList();
        var b = new StringBuilder();
        b.AppendLine("# Setting up a sandbox for proctor on this machine").AppendLine();
        b.AppendLine("You are helping the user make this machine able to run proctor's agent arms (Claude Code, Codex, Qwen Code, "
            + "nb with tools). Those run through Harbor, in containers, so the agent under test cannot touch the user's files, keys "
            + $"or settings. `proctor setup` wrote this file on {today:yyyy-MM-dd} from what it could see; it does not know this "
            + "user's constraints. Find them out before acting.").AppendLine();

        b.AppendLine("## What the user said").AppendLine();
        b.AppendLine("- Sandbox: on this machine (step 2 of `proctor setup`).");
        b.AppendLine(wanted.Count == 0
            ? "- Agent arms wanted: none chosen yet (steps 3 and 4 come after this one; `proctor setup --check` has the latest)."
            : $"- Agent arms wanted: {string.Join(", ", wanted)}.");
        b.AppendLine();

        b.AppendLine("## What setup observed").AppendLine();
        b.AppendLine("Facts at the time of writing, not a diagnosis. Re-check anything you rely on.").AppendLine();
        b.AppendLine("| | |").AppendLine("|---|---|");
        foreach (var o in seen) b.AppendLine($"| {o.What} | {o.Found.Replace("|", "\\|")} |");
        b.AppendLine();

        b.AppendLine("## What proctor needs").AppendLine();
        b.AppendLine("The requirement, not the route. Any route that ends here is fine.").AppendLine();
        b.AppendLine("- A container engine Harbor drives: Docker with the Compose v2 and buildx plugins, or rootless podman with its "
            + "API socket running and docker-compose v2 behind `podman compose`.");
        b.AppendLine("- For podman: an unqualified-search registry (the egress sidecar is `FROM gogost/gost`) and "
            + "`compose_warning_logs = false` (Harbor reads the compose banner as agent output).");
        b.AppendLine("- uv on PATH; it runs the pinned Harbor.");
        b.AppendLine(engine is null
            ? "- `harbor.engine` in `suites/proctor.json` naming the engine (`docker` is the default; setup found neither, so wrote nothing)."
            : $"- `harbor.engine` in `suites/proctor.json` naming the engine (`docker` is the default; setup wrote `{engine}` here, from what it found).");
        b.AppendLine();
        b.AppendLine("Why each of these exists, and the known traps (Ubuntu's pasta AppArmor profile among them), are in "
            + "`docs/podman.md` for podman and `docs/configuration.md`, \"Agent arms\", for both.");
        if (OperatingSystem.IsWindows())
            b.AppendLine().AppendLine("This is Windows. Harbor on a Windows host has not been tried with proctor; Docker Desktop on WSL2 is the "
                + "likely route, and `docs/podman.md` is Linux-only. Say so to the user before they spend effort on it.");
        b.AppendLine();

        b.AppendLine("## How to proceed").AppendLine();
        b.AppendLine("- Ask before you change anything. Learn what the user may do on this machine: whether it is a managed work "
            + "laptop, whether installing packages or system services needs approval, whether Docker Desktop's licence is an issue "
            + "for their employer, whether there is a proxy or a private registry mirror. The right route depends on the answers, "
            + "and a route that needs a ticket the user cannot get is not a route.");
        b.AppendLine("- Prefer what is already installed" + (engine is null ? "." : $". Here {engine} is present, so finishing it is likely cheaper than adding another engine, unless the user says otherwise."));
        b.AppendLine("- Propose the steps as a short plan and let the user approve it. Run nothing that needs root without saying so "
            + "first. Do not put any API key on a command line or in a file other than the user's proctor env file.");
        b.AppendLine("- If this machine cannot be made to work within the user's constraints, say so plainly. Suites with no agent "
            + "(the built-in harness) need no sandbox, and a hosted sandbox is planned; `proctor setup` records either choice.");
        b.AppendLine();

        b.AppendLine("## Done means").AppendLine();
        b.AppendLine("`proctor setup --check` reports the engine and uv found. The full proof, which runs every vendor agent against "
            + "a scripted stub model at no cost, is:").AppendLine();
        b.AppendLine("    PROCTOR_CONFORMANCE=1 dotnet test --no-build --filter ConformanceTests").AppendLine();
        b.AppendLine("It takes about half an hour; offer it, do not run it unasked.");
        return b.ToString();
    }

    static bool Found(IReadOnlyList<Observation> seen, string what) =>
        seen.FirstOrDefault(o => o.What == what) is { Found: not "not found" };

    /// <summary>The command's first line of output, or "not found" when it cannot start, or its first error line when it fails.</summary>
    static string Version(string command, params string[] args)
    {
        if (Runner.FindOnPath(command) is null) return "not found";
        var result = Subprocess.Run(command, [.. args], Directory.GetCurrentDirectory(), timeout: TimeSpan.FromSeconds(10));
        var text = result.ExitCode == 0 ? result.Stdout : (result.Stderr.Length > 0 ? result.Stderr : result.Stdout);
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        return result.ExitCode == 0 ? line : $"exit {result.ExitCode}{(line.Length > 0 ? ": " + line : "")}";
    }
}
