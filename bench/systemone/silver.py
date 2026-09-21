#!/usr/bin/env python3
"""Labels from the Claude Code session (or a subagent it spawns), and the adjudication list.

    silver.py todo items.jsonl rubric.json [question]     print the unlabelled items, windows included, for the labeller to read
    silver.py apply items.jsonl labels.jsonl              write labels: one line per {id, question, label, quote?, reason}
    silver.py adjudicate items.jsonl results/<run>.answers.jsonl   where the decider and the labels differ, or it was unsure

No paid model is called here. The labeller is the session model, recorded per label
in "labeller" with its reason in "silver_reason" so the human adjudicates only the
disagreements. A label a human entered (labeller "human") is never overwritten.
"""
import json, os, sys

def load(path): return [json.loads(l) for l in open(path) if l.strip()]
def save(path, items):
    with open(path + ".tmp", "w") as f:
        for it in items: f.write(json.dumps(it, ensure_ascii=False) + "\n")
    os.replace(path + ".tmp", path)

def todo(items_path, rubric_path, only=None):
    items, rubric = load(items_path), json.load(open(rubric_path))
    descriptions = rubric.pop("windows")
    questions = [only] if only else list(rubric)
    for it in items:
        pending = [q for q in questions if q not in it["gold"] and all(it["windows"].get(x) for x in rubric[q]["windows"])]
        if not pending: continue
        print(f"\n{'#' * 80}\n# {it['id']}  questions: {', '.join(pending)}")
        shown = set()
        for q in pending:
            for n in rubric[q]["windows"]:
                if n in shown: continue
                shown.add(n); v = it["windows"][n]
                print(f"\n## {n}: {descriptions[n]}\n" + ("\n".join(v) if isinstance(v, list) else v))
        for q in pending:
            spec = rubric[q]; opts = list(spec["criteria"]) if spec["type"] == "choice" else ["true", "false"]
            print(f"\n## question {q}: {spec['instructions']}\n" + "\n".join(f"- {o}: {spec['criteria'].get(o) or ''}" for o in opts))

def apply(items_path, labels_path, labeller):
    items = {it["id"]: it for it in load(items_path)}
    n = 0
    for lab in load(labels_path):
        it = items[lab["id"]]; q = lab["question"]
        it.setdefault("labeller", {}); it.setdefault("silver_reason", {})
        if it["labeller"].get(q) == "human": continue
        it["gold"][q] = lab["label"]; it["labeller"][q] = labeller; it["silver_reason"][q] = lab.get("reason", "")
        if "quote" in lab:
            fm = it["windows"].get("final_message") or ""
            it.setdefault("gold_quote", {})[q] = lab["quote"] if lab["quote"] and lab["quote"] in fm else None
        n += 1
    save(items_path, list(items.values()))
    print(f"{n} labels applied as {labeller}", file=sys.stderr)

def adjudicate(items_path, answers_path):
    items = {it["id"]: it for it in load(items_path)}
    for row in load(answers_path):
        it = items.get(row["id"])
        if not it: continue
        for q, a in row["answers"].items():
            if q in it["gold"] and (a["label"] != it["gold"][q] or a["confidence"] < 0.9):
                print(f"{row['id']}\n  {q}: decider={a['label']}@{a['confidence']:.2f}  gold={it['gold'][q]} [{it.get('labeller', {}).get(q, 'human')}]\n  {it.get('silver_reason', {}).get(q, '')}")

if __name__ == "__main__":
    cmd = sys.argv[1]
    if cmd == "todo": todo(sys.argv[2], sys.argv[3], sys.argv[4] if len(sys.argv) > 4 else None)
    elif cmd == "apply": apply(sys.argv[2], sys.argv[3], os.environ.get("LABELLER", "session:claude-fable-5.1"))
    elif cmd == "adjudicate": adjudicate(sys.argv[2], sys.argv[3])
