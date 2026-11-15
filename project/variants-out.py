#!/usr/bin/env python3
"""Step 2 of project/plans/exam-variant-attempt-removal.md, run once: every tracked exam.json that declares its
variants inline gives each one its own file, variants/<id>.json beside it, and gains `attempts`.

A variant's `description` becomes its `note`; its `attempts` leaves it. The exam's `attempts` is the smallest any of
its variants had (an exam whose variants had none gets none, so 1), and the script says so where they differed, so
no paid variant is attempted more often than before. exam.json is edited as text, so the rest of it keeps its layout.
Deleted in the commit after the one that uses it.
"""
import json
import pathlib
import re
import subprocess

root = pathlib.Path(subprocess.check_output(["git", "rev-parse", "--show-toplevel"], text=True).strip())
files = [root / f for f in subprocess.check_output(["git", "ls-files"], cwd=root, text=True).split("\n")
         if re.search(r"(^|/)exam\.json$", f)]

for file in files:
    text = file.read_text()
    exam = json.loads(text)
    if "variants" not in exam:
        continue
    variants = exam["variants"]

    # The span of "variants": [...], from the start of its line to the end of the line its closing bracket is on.
    key = re.search(r'^[ \t]*"variants"\s*:\s*', text, re.M)
    _, end = json.JSONDecoder().raw_decode(text, key.end())
    rest = text[end:]
    assert rest.lstrip().startswith(","), f"{file}: variants is the last key"
    end += rest.index(",") + 1
    end = text.index("\n", end) + 1
    text = text[:key.start()] + text[end:]

    counts = [v.get("attempts", 1) for v in variants]
    if any("attempts" in v for v in variants):
        attempts = min(counts)
        if len(set(counts)) > 1:
            print(f"{file.relative_to(root)}: variants had {sorted(set(counts))} attempts; the exam's attempts is {attempts}")
        after = re.search(r'^  "description": .*\n', text, re.M) or re.search(r'^  "id": .*\n', text, re.M)
        text = text[:after.end()] + f'  "attempts": {attempts},\n' + text[after.end():]
    file.write_text(text)

    out = file.parent / "variants"
    out.mkdir(exist_ok=True)
    for v in variants:
        converted = {"id": v["id"]}
        if "description" in v:
            converted["note"] = v["description"]
        converted.update({k: value for k, value in v.items() if k not in ("id", "description", "attempts")})
        (out / f"{v['id']}.json").write_text(json.dumps(converted, indent=2, ensure_ascii=False) + "\n")
    print(f"{file.relative_to(root)}: {len(variants)} variant(s) to {out.relative_to(root)}/")
