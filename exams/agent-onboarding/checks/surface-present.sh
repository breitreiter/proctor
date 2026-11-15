#!/usr/bin/env bash
# Validity: was the onboarding surface there to be used? The fixture's image clones and builds nb and proctor at
# pinned revisions, puts both on PATH and installs proctor's skill where Claude Code and Qwen Code look for
# skills. An attempt whose image lacked any of it measured something else and does not count. The verifier runs
# in a fresh container of the same image. The reason names the proctor revision, so the record says which
# surface an attempt saw.
set -uo pipefail
proctor_dir=/opt/proctor
v=$(proctor --version 2>&1) || { echo "proctor does not run: $(head -1 <<<"$v")"; exit 1; }
n=$(nb --version 2>&1) || { echo "nb does not run: $(head -1 <<<"$n")"; exit 1; }
config=$(mktemp); trap 'rm -f "$config"' EXIT
echo '{"Harness": "nb", "ActiveProvider": "Mock", "ChatProviders": [{"Name": "Mock", "Response": "mock-ok"}]}' >"$config"
mock=$(printf 'tools none\nrun hello\n' | nb --output jsonl --config "$config" - 2>&1)
grep -q 'mock-ok' <<<"$mock" || { echo "nb's Mock provider does not answer, so no exam can be shaken down: $(grep -m1 -iE 'error|provider' <<<"$mock" | cut -c1-120)"; exit 1; }
skill="$proctor_dir/skills/proctor-exam/SKILL.md"
[ -f "$skill" ] || { echo "no $skill"; exit 1; }
for d in "$HOME/.claude/skills" "$HOME/.qwen/skills"; do
  cmp -s "$skill" "$d/proctor-exam/SKILL.md" || { echo "the skill is not installed in $d"; exit 1; }
done
[ -f "$proctor_dir/README.md" ] && [ -f "$proctor_dir/docs/configuration.md" ] || { echo "proctor's docs are missing from $proctor_dir"; exit 1; }
rev=$(git -C "$proctor_dir" log -1 --format=%s 2>/dev/null)
echo "$v (${rev:-unknown revision}), nb $n, skill installed"; exit 0
