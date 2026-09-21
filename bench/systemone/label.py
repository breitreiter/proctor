#!/usr/bin/env python3
"""Keystroke labelling loop over items.jsonl, one question at a time.

    label.py items.jsonl rubric.json [question]     label unlabelled items (all questions by default)
    extract.py runs/* | label.py merge items.jsonl  add new items, keeping gold on ones already there

Keys: the option's number, s to skip, q to quit. For `stance` a second keystroke
picks the sentence of the answer that supports the label (0 = none), stored as
gold_quote, for the evidence-by-selection test.
"""
import json, os, re, sys, textwrap

def load(path):
    return [json.loads(l) for l in open(path)] if os.path.exists(path) else []

def save(path, items):
    tmp = path + ".tmp"
    with open(tmp, "w") as f:
        for it in items: f.write(json.dumps(it, ensure_ascii=False) + "\n")
    os.replace(tmp, path)

def sentences(text):
    return [s for s in re.split(r"(?<=[.!?])\s+|\n+", text or "") if s.strip()]

def show(title, body, width=100):
    print(f"\n--- {title} ---")
    print(textwrap.indent(textwrap.fill(body, width) if "\n" not in (body or "") else (body or ""), "  ")[:6000])

def ask(prompt, valid):
    while True:
        k = input(prompt).strip()
        if k in valid: return k

def label(items_path, rubric_path, only=None):
    items, rubric = load(items_path), json.load(open(rubric_path))
    questions = [only] if only else [q for q in rubric if q != "windows"]
    for it in items:
        for q in questions:
            if q in it["gold"]: continue
            spec = rubric[q]
            w = it["windows"]
            print("\n" + "=" * 100 + f"\n{it['id']}  [{q}]")
            for name in spec["windows"]:
                v = w.get(name)
                show(f"{name}: {rubric['windows'][name]}", "\n".join(v) if isinstance(v, list) else (v or "(empty)"))
            print(f"\n{spec['instructions']}")
            opts = list(spec["criteria"]) if spec["type"] == "choice" else ["true", "false"]
            for i, o in enumerate(opts, 1): print(f"  {i}. {o}: {spec['criteria'].get(o, '')}")
            k = ask("> ", {str(i) for i in range(1, len(opts) + 1)} | {"s", "q"})
            if k == "q": save(items_path, items); return
            if k == "s": continue
            it["gold"][q] = opts[int(k) - 1] if spec["type"] == "choice" else (k == "1")
            if q == "stance" and w.get("final_message"):
                ss = sentences(w["final_message"])
                for i, s in enumerate(ss, 1): print(f"  {i:2}. {s[:120]}")
                k = ask("quote (0 = none)> ", {str(i) for i in range(0, len(ss) + 1)})
                it.setdefault("gold_quote", {})[q] = ss[int(k) - 1] if k != "0" else None
            save(items_path, items)
    save(items_path, items)

def merge(items_path):
    old = {it["id"]: it for it in load(items_path)}
    new = [json.loads(l) for l in sys.stdin if l.strip()]
    for it in new:
        if it["id"] in old:
            it["gold"] = old[it["id"]]["gold"]
            if "gold_quote" in old[it["id"]]: it["gold_quote"] = old[it["id"]]["gold_quote"]
    keep = [it for it in old.values() if it["id"] not in {n["id"] for n in new}]
    save(items_path, keep + new)
    print(f"{len(new)} extracted, {len(keep)} kept, {sum(1 for it in keep + new if it['gold'])} labelled", file=sys.stderr)

if __name__ == "__main__":
    if sys.argv[1] == "merge": merge(sys.argv[2])
    else: label(sys.argv[1], sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else None)
