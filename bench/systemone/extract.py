#!/usr/bin/env python3
"""Turn proctor cells into bench items: the windows a decider reads, self-contained.

Windows, described once in rubric.json under "windows" so the human and the model
read the same sentence: task_prompt, final_message (the whole of the assistant's
last message, sent after its final tool call), tool_calls (every call in order),
diff.

    extract.py runs/<experiment> [...] > items.jsonl

Items already in an existing items.jsonl keep their gold: pipe through merge with
    extract.py runs/* | ./label.py merge items.jsonl
"""
import json, os, sys

def windows(cell):
    events = [json.loads(l) for l in open(os.path.join(cell, "transcript.jsonl")) if l.strip()]
    prompt = next((e["text"] for e in events if e["type"] == "user"), None)
    answers = [e["text"] for e in events if e["type"] == "assistant_text"]
    calls = []
    for e in events:
        if e["type"] == "tool_call":
            args = json.dumps(e.get("arguments") or {}, ensure_ascii=False)
            calls.append(f'{e["name"]} {args[:200]}')
    trailer = next((e for e in events if e["type"] == "result"), {})
    diff_path = os.path.join(cell, "diff.patch")
    diff = open(diff_path).read() if os.path.exists(diff_path) else None
    return {
        "task_prompt": prompt,
        "final_message": answers[-1] if answers else None,
        "tool_calls": calls,
        "diff": diff,
        "exit_reason": trailer.get("exit_reason"),
    }

def cells(exp):
    for arm in sorted(os.listdir(exp)):
        if not os.path.isdir(os.path.join(exp, arm)): continue
        for case in sorted(os.listdir(os.path.join(exp, arm))):
            for sample in sorted(os.listdir(os.path.join(exp, arm, case))):
                cell = os.path.join(exp, arm, case, sample)
                if os.path.exists(os.path.join(cell, "transcript.jsonl")):
                    yield arm, case, sample, cell

for exp in sys.argv[1:]:
    exp = exp.rstrip("/")
    for arm, case, sample, cell in cells(exp):
        item = {
            "id": f"{os.path.basename(exp)}/{arm}/{case}/{sample}",
            "source": {"experiment": os.path.basename(exp), "arm": arm, "case": case, "sample": sample},
            "windows": windows(cell),
            "gold": {},
        }
        print(json.dumps(item, ensure_ascii=False))
