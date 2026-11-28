# Front page copy, sections 1 to 4

## 1. Hero

# Find out whether agents can use what you ship

Auspex is an experiment manager and runner for AI. It runs real coding agents against your MCP server, CLI, API, or docs, then helps you find faults and friction.

Auspex is free under the MIT license and runs locally. It's modular and built to be remixed.

[See a report] [What you'll need]

## 2. Problem and who it is for

## Agents use your product too

Coding agents read your docs and call your API, and teams now ship features for them, like MCP servers, skills, and AGENTS.md. Mathias Biilmann of Netlify [named this agent experience, or AX](https://biilmann.blog/articles/introducing-ax/), in 2025.

Most teams are managing AX through vibes; if it works with someone's Claude Code, it ships. But agent behavior shifts with the model family, version, the harness and whatever else is in its context:

**Vercel: the skill went unopened in 56% of eval cases**
On Next.js 16 tasks the pass rate with the skill stayed at the 53% baseline, and an 8KB docs index placed in AGENTS.md reached 100%.
[Source: Vercel blog](https://vercel.com/blog/agents-md-outperforms-skills-in-our-agent-evals)

**Microsoft: a shipped upgrade script ran 0 times in 10**
The SharePoint Framework docs came with a ready-to-run upgrade script, and no agent piped it to a shell.
[Source: Microsoft, Agent experience](https://microsoft.github.io/agent-experience/)

**Stripe: a 400 was reported as success**
Stripe holds that "a mostly correct integration is a failure". In its 11 integration environments, some agents passed data that did not exist and, after a 400 came back, declared the task complete.
[Source: Stripe blog](https://stripe.com/blog/can-ai-agents-build-real-stripe-integrations)

### Who it is for

Auspex is for people who:

- ship an MCP server, a CLI, an API or docs that agents use
- maintain a skill, a plugin or a CLAUDE.md
- run an assistant inside a product, or an agent as a headless job
- decide which agent and model a team works with, and want to know whether a change helped

It's not a great fit for benchmarks. It's designed for tool-using agents in complex environments, so may be overkill for some cases.

## 3. What sets it apart

## What sets it apart

**Agents run in their own harness.**
Models are tuned to their own harness, so Claude Code, Codex and Qwen Code each run natively through Harbor, an open-source harness runner. Each attempt gets its own container, with controlled network access.

**All the grading levers you'd expect.**
Auspex includes a toolbox of configurable, deterministic checks and metrics calculated over each run. For more complex classification tasks, you can use a decider model like Jev or Clef. Auspex supports llm-as-judge, but with guardrails. Judge models need to quote transcript evidence in support of assertions and we run multiple passes to ensure AI-generated findings are consistent.

**Simple, flexible experiment management.**
Use as much rigor as you need for your project. Start with just a bare prompt and review the transcript to see what's actually happening. Layer in checks to watch for bad behavior and report on them. Try making some changes and compare the new result. Use axes to break your changes into controlled chunks, so you can measure the individual effects and matrix coverage. Group changes into named, controlled configurations to track release proposals. Write and track predictions to justify and steer your eval AI spend.

**Multi-turn is built in.**
Some assistants should ask before they act:

> "I'm looking for some shoes."
> "What kind?"
> "Running shoes, for trails."

Auspex can run a user-simulator agent that provides sensible answers from a controlled oracle data sheet, allowing your evals to handle complex conversations.

## 4. How it works

## How it works

1. **Plan the exam with your coding agent.** auspex ships a skill for this. Your agent interviews you about what you ship and what a good outcome looks like, then writes a plan and waits for your approval.

2. **Prove the checks for $0.** A scripted stand-in for the model and a set of shakedown scripts show that each check passes a good attempt and fails a bad one.

3. **Try a cheap or local model first.** A vague prompt or a broken fixture shows up here, before the expensive run.

4. **Run the agents.** Each task gets several attempts per configuration, each in its own container.

5. **Read the report, then the transcripts.** Read a few transcripts even when every row is green. Your first checks will be bad, because they're guesses. Once you see what the agents are doing, you can sharpen the checks.

6. **Change one thing and run again.** Edit the docs or move to another model, then compare. The report says whether the two differ or whether there are too few attempts to tell.

## 6. What you'll need

## What you'll need

| What | What it's for | Can you do without it? |
|---|---|---|
| Your coding agent | Plans and builds exams with you | Yes, though the docs are written for an agent |
| Linux, rootless podman, uv | Containers for the agents you measure. | Required for vendor harness runs, optional for the built-in harness |
| .NET 10 SDK and git | Building from source | No, until binaries ship |
| Keys for vendor harnesses | Anthropic for Claude Code, OpenAI for Codex | Yes. The built-in harness can run on a local model. |
| Key for a judge model | Checks where a model reads the transcript | Yes, if every check is deterministic |
| Key for a decider model | Classification checks, where a small model returns a label and a probability | Yes, by leaving those checks out |

Pick a judge from a model family you aren't measuring, because a model tends to favor its own family's work. With Claude and GPT under test, that means an open model like GLM or Kimi.

If you can't meet a requirement, but still want to use Auspex, check out the next section.

[Remix]

## 8. Remix, license, repository

## Remix manifesto

"This all sounds great, but can't I just vibe-code my own tool?"

You can and you should. Auspex is built to be remixed:

- Each feature has clear seams and documented, versioned formats. Borrow the parts you need or fold Auspex into a larger solution.
- Auspex is free under the MIT license. Take what you need, even if you're releasing under a stricter license.

The remix guide is written for your coding agent. In addition to general guidance, it includes recipes for common modifications (like new container engines).

[Remix guide] [Repository]

## Notes

Adversarial pass done, 2026-11-27.

### Citations checked (all five pages loaded, fetched 2026-11-27)

- **Vercel**, https://vercel.com/blog/agents-md-outperforms-skills-in-our-agent-evals. Matches. The page says "In 56% of eval cases, the skill was never invoked", baseline 53%, skill by default 53%, AGENTS.md index 100%. I added two details from the page: the tasks were on Next.js 16 APIs, and the index was 8KB. Not used: the skill with explicit instructions reached 79%.
- **Microsoft**, https://microsoft.github.io/agent-experience/. **Does not match the sheet on the run count.** The sheet says the upgrade task ran five times per model and the script executed in none. The page says "In zero runs out of ten did the agent pipe the script to a shell" (SharePoint Framework upgrade) and names no model on that card. The copy says 0 of 10. The 3.7x figure matches: the page gives $2.01 per run for Claude Sonnet 5 against $0.55 for Sonnet 4.6 on SPFx upgrades, with completion 100% against 60%, five runs per model per scenario. The copy gives the completion rates beside the 3.7x, since a cost rise that came with a completion rise reads differently from a bare multiplier.
- **Stripe**, https://stripe.com/blog/can-ai-agents-build-real-stripe-integrations. Eleven environments: matches. The quote is exact; the full sentence is "When it comes to businesses running on Stripe, a mostly correct integration is a failure; payments require 100% accuracy." **Two softenings against the sheet.** Grading is described as mostly deterministic tests that exercise the finished software through API calls or UI automation, with only some graders inspecting Stripe API objects, so the copy leaves the grading method out. And the 400 behaviour is attributed to some agents on the SDK upgrade tasks, so the copy says "some agents".
- **Biilmann**, https://biilmann.blog/articles/introducing-ax/. Mathias Biilmann, 28 January 2025, titled "Introducing AX". The page introduces the term and does not use the word "coined", so the copy says "named". The page does not state his title; it speaks of Netlify as "we".
- **Microsoft devrel**, https://developer.microsoft.com/blog/what-is-agent-experience-ax/. Published 6 October 2026 by Waldek Mastykarz, Principal Developer Advocate. Propensity ("does the agent find your technology and choose it?") and efficacy ("once the agent uses your technology, does it use it correctly?") match.

I read these pages through a fetch tool that summarises, so the quoted strings above should be eyeballed once against the live pages before launch, the Microsoft card first.

### Left out, and why

- **app.auspex.bot, the invite-only alpha, Google sign-in, share links.** The hero has about 50 words and the "See a report" button carries it. Sign-in details belong on the page the button opens.
- **The GitHub URL.** The repository is being renamed, and a link in copy would go stale.
- **nb and its Mock provider by name.** A front-page reader does not know nb. Step 2 says "a scripted stand-in for the model".
- **Harbor is named once**, with a four-word gloss, and not again in section 4.
- **The viewer as a separate thing from the CLI.** The hero says "the report" and the screenshot shows it.
- **"Local CLI".** Covered by "exams are files in your git repository"; a line about where it runs fits better under "What you'll need".
- **The vocabulary definitions as a block.** Exam, task, check, attempt, axis, configuration, bank and prediction are each used in a sentence that shows what they mean. "Fixture" is used once, undefined, in step 3; say if it should go.
- **The 79% Vercel figure** (skill with explicit instructions). The card is already three numbers long.

### Things I added that the sheet does not state

- Step 2 says the shakedown shows each check "passes a good attempt and fails a bad one". The sheet says only that the checks are proved for free. Cut the clause if that is not what the shakedown scripts do.
- Step 3 says a vague prompt or broken fixture shows up on the cheap model. That is my reason for the step; the sheet gives none.
- Step 4 says "several attempts per configuration", from the product description's "several times each".

### Sections 6 and 8

Left out, and why:

- **Qwen Code in the podman row.** Added in the adversarial pass: Claude Code, Codex and Qwen Code all need the sandbox.
- **app.auspex.bot and "the project holds nothing of yours".** Neither is something a reader needs before installing. It fits a privacy line near "See a report".
- **nb by name.** "The bundled harness", as in the sheet and as sections 1 to 4 avoid the name.
- **Keys entered at a hidden prompt, never through the agent.** Drafted and cut for length. A reader counting keys will ask where they go, so it is worth a line on the setup page.
- **"Fewer agents, fewer keys."** The table's fourth row says a key per agent you measure.
- **"Released binaries later" as its own sentence.** The table cell "No, until binaries ship" carries it.
- **Typesafe, and the provider lists in full.** The judge paragraph names OpenRouter and z.ai; the four-key paragraph names only OpenRouter, the one that covers both. The section was over length and I could not confirm Typesafe.
- **"The repo is written for an agent to work in" and "someone who needs Docker or macOS can own a recipe".** Cut for length from section 8; both belong on the remix page.
- **"People with coding agents increasingly build their own tools."** A trend claim with no source, and the section reads the same without it.
- **"Small on purpose" and the podman platform line in section 8.** Section 6 already states the supported platform; section 8 keeps only what is new there, that Docker and macOS are unmaintained.
- **"Auspex unchanged" in the beside paragraph.** Implied by "an upgrade is a version bump".
- **The link from section 8's stance back to section 6.** One direction is enough.

Added or changed beyond the sheet:

- **"A model tends to favor its own family's work"** where the sheet says "is biased". Softer, and closer to what the published self-preference results support.
- **"One key there can cover the last two."** The sheet says one provider can cover both; the copy does not go on to say "three keys", since that is arithmetic the reader can do and a number I would be inventing.
- **"Still ends in a full report"** for the playground exam. The sheet says it "needs nothing at all"; the copy says no model and no key, since it still needs the build tools in the table.
- **"So an upgrade is a version bump."** The "so" ties the written promise to the upgrade; the sheet lists them as separate facts.
- **"Covering every setup is out of scope"** for "not to be everything to everyone", reworded to avoid the contrast form.
- **The closing line "Auspex is free under the MIT license."** Repeats the hero's wording so the section ends on a plain fact above the links.

Adversarial pass on sections 6 and 8 done, 2026-11-28.
