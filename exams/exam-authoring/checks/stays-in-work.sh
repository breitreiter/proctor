#!/usr/bin/env bash
# Validity: did the agent stay inside its checkout? Fails on the first tool call that names a path
# outside the checkout (system paths aside), by an absolute path or by climbing with `..` from where
# the command stood. Reaching the repository root would find the exam, the tasks and the fixture's
# checker, so such an attempt does not count; it is not a capability failure. The variant's bundle
# is there to be read. The checkout is where the model was told it is: $PROCTOR_WORK_MOUNT, which a
# runner may put somewhere other than $PROCTOR_WORK on the host.
#
# It reads a shell command roughly, not as a shell would: words, quotes, `cd`. Three things are not
# paths and are skipped: a heredoc's body (file content), a quoted word with a space or a pattern
# character in it (a sed, grep or awk script), and a quoted word that starts and ends with `/`
# (a pattern such as '/repo/'). `cd` to a variable or a subshell's `cd` is not followed.
set -uo pipefail
export WORK=${PROCTOR_WORK_MOUNT:-$PROCTOR_WORK} BUNDLE=${PROCTOR_BUNDLE_MOUNT:-/nonexistent/bundle}
exec python3 - "$PROCTOR_TRANSCRIPT" <<'PY'
import json, os, posixpath, re, sys

work, bundle = os.environ["WORK"].rstrip("/"), os.environ["BUNDLE"].rstrip("/")
roots = [work, bundle, "/dev", "/tmp", "/usr", "/bin", "/etc", "/proc", "/opt", "/nb", "/var", "/root/.nuget"]
home = re.compile(r"^/home/[^/]+/\.(nuget|dotnet)(/|$)")
prose = {"content", "old_string", "new_string", "text", "description"}
heredoc = re.compile(r"<<-?\s*(['\"]?)(\w+)\1.*?\n.*?^\s*\2\s*$", re.S | re.M)
word = re.compile(r"""'[^']*'|"(?:\\.|[^"\\])*"|[^\s;&|()<>'"]+""")

def strings(node):
    if isinstance(node, str): yield node
    elif isinstance(node, list):
        for item in node: yield from strings(item)
    elif isinstance(node, dict):
        for key, value in node.items():
            if key not in prose: yield from strings(value)

def inside(path):
    return bool(home.match(path)) or any(path == r or path.startswith(r + "/") for r in roots)

def outside(text):
    cwd, previous = work, ""
    for match in word.finditer(heredoc.sub("", text)):
        token = match.group()
        quoted = token[0] in "'\""
        w = token[1:-1] if quoted else token
        if quoted and (re.search(r"[\s*\[\]\\^$|?]", w) or (len(w) > 1 and w.startswith("/") and w.endswith("/"))):
            previous = token; continue
        if not w.startswith("/") and "=" in w: w = w.split("=", 1)[1]
        climbs = w == ".." or w.startswith("../") or "/../" in w or w.endswith("/..")
        if "$" not in w and "~" not in w and (w.startswith("/") or climbs):
            path = posixpath.normpath(posixpath.join(cwd, w))
            if path == "/": return "names the filesystem root" if w.startswith("/") else f"climbs out of the checkout: {w}"
            if not inside(path): return f"{'reaches' if w.startswith('/') else 'climbs'} outside the checkout: {path}"
        if previous == "cd" and "$" not in w and "~" not in w and w != "-":
            cwd = posixpath.normpath(posixpath.join(cwd, w))
        previous = token
    return None

n = 0
for line in open(sys.argv[1]):
    if not line.strip(): continue
    event = json.loads(line)
    if event.get("type") != "tool_call": continue
    n += 1
    for text in strings(event.get("arguments") or {}):
        if (why := outside(text)):
            print(f"call {n} ({event.get('name')}) {why}"); sys.exit(1)
print(f"{n} tool calls, all inside the checkout" if n else "no tool calls")
PY
