#!/usr/bin/env python3
"""Run a rubric over items.jsonl against a System One endpoint and score it.

    bench.py --backend jev   [--url $LLM_GATEWAY/cf/workers-ai/run/typesafe/jev] items.jsonl rubric.json
    bench.py --backend llama [--url $LLM_GATEWAY/local/v1/chat/completions] [--temperature-file t.json] items.jsonl rubric.json

Writes results/<backend>-<rubric>.answers.jsonl (one line per item, per-question
answer, probabilities, confidence, input tokens) and results/<backend>-<rubric>.metrics.json.
LLM_GATEWAY is the base URL of the gateway the default URLs hang off; the bearer token is $LLM_GATEWAY_KEY. Standard library only.
"""
import argparse, json, math, os, sys, time, urllib.error, urllib.request
from collections import defaultdict

GATEWAY = os.environ.get("LLM_GATEWAY", "http://localhost:8086")
JEV_URL = f"{GATEWAY}/cf/workers-ai/run/typesafe/jev"
LLAMA_URL = f"{GATEWAY}/local/v1/chat/completions"

def post(url, body, timeout=120):
    req = urllib.request.Request(url, data=json.dumps(body).encode(), method="POST",
        headers={"content-type": "application/json", "user-agent": "proctor-bench/0.1", "authorization": f"Bearer {os.environ['LLM_GATEWAY_KEY']}"})
    for attempt in range(3):
        try:
            with urllib.request.urlopen(req, timeout=timeout) as r: return json.load(r)
        except urllib.error.HTTPError as e:
            err = RuntimeError(f"HTTP {e.code}: {e.read()[:400].decode(errors='replace')}"); time.sleep(2 * (attempt + 1))
        except Exception as e:
            err = e; time.sleep(2 * (attempt + 1))
    raise err

def options(spec):
    return list(spec["criteria"]) if spec["type"] == "choice" else ["true", "false"]

def gated(spec, w):
    """The static gate: a question whose window is empty never reaches a model."""
    for name in spec["windows"]:
        v = w.get(name)
        if v is None or (isinstance(v, str) and not v.strip()) or (isinstance(v, list) and not v): return name
    return None

# --- backends: each returns {question: {"label", "probabilities", "confidence"}} plus input tokens

def run_jev(url, w, questions, descriptions):
    """One call per distinct window set. The state is a JSON object of the windows, as Jev's docs
    show, with the same one-sentence description of each window the labeller prints."""
    out, tokens = {}, 0
    by_windows = defaultdict(dict)
    for q, spec in questions.items(): by_windows[tuple(spec["windows"])][q] = spec
    for names, qs in by_windows.items():
        state = {n: {"what_this_is": descriptions[n], "content": w[n]} for n in names}
        body = {"state": state, "questions": {q: {k: v for k, v in s.items() if k != "windows"} for q, s in qs.items()}}
        r = post(url, body)
        if not r.get("result"): raise RuntimeError(json.dumps(r)[:300])
        tokens += r["result"].get("usage", {}).get("input_tokens", 0)
        for q, a in r["result"]["answers"].items():
            if a["type"] == "noul":
                p = a["noul"]; out[q] = {"label": p >= 0.5, "probabilities": {"true": p, "false": 1 - p}, "confidence": max(p, 1 - p)}
            else:
                out[q] = {"label": a["choice"], "probabilities": a["probabilities"], "confidence": a.get("confidence", max(a["probabilities"].values()))}
    return out, tokens

def compile_prompt(spec, w, descriptions):
    """OpenJev's recipe: state, question, numbered options, one label token."""
    parts = []
    for n in spec["windows"]:
        v = w[n]; parts.append(f"### {n}\n{descriptions[n]}\n\n" + ("\n".join(v) if isinstance(v, list) else v))
    opts = options(spec)
    if spec["type"] == "choice":
        listed = "\n".join(f"{i}. {o}: {spec['criteria'].get(o) or ''}" for i, o in enumerate(opts, 1))
        ask = f"{spec['instructions']}\n\n{listed}\n\nAnswer with the number only."
        labels = {str(i): o for i, o in enumerate(opts, 1)}
    else:
        ask = f"{spec['instructions']}\nYes: {spec['criteria'].get('true','')}\nNo: {spec['criteria'].get('false','')}\n\nAnswer with exactly one word, Yes or No."
        labels = {"Yes": "true", "No": "false"}
    return "\n\n".join(parts) + "\n\n" + ask, labels

def run_llama(url, w, questions, temps, descriptions):
    out, tokens = {}, 0
    for q, spec in questions.items():
        prompt, labels = compile_prompt(spec, w, descriptions)
        body = {"messages": [{"role": "user", "content": prompt}], "max_tokens": 1, "temperature": 0,
                "logprobs": True, "top_logprobs": 20, "chat_template_kwargs": {"enable_thinking": False}}
        r = post(url, body, timeout=600)
        tokens += r.get("usage", {}).get("prompt_tokens", 0)
        top = r["choices"][0]["logprobs"]["content"][0]["top_logprobs"]
        lp = {}
        for t in top:
            tok = t["token"].strip()
            if tok in labels: lp[labels[tok]] = max(lp.get(labels[tok], -1e9), t["logprob"])
        T = temps.get(spec["type"], 1.0)
        if not lp: lp = {o: -1e9 for o in options(spec)}
        for o in options(spec): lp.setdefault(o, -30.0)
        z = max(v / T for v in lp.values()); den = sum(math.exp(v / T - z) for v in lp.values())
        probs = {o: math.exp(v / T - z) / den for o, v in lp.items()}
        label = max(probs, key=probs.get)
        out[q] = {"label": label == "true" if spec["type"] == "noul" else label, "probabilities": probs, "confidence": max(probs.values())}
    return out, tokens

# --- metrics

def kappa(pairs):
    n = len(pairs)
    if n == 0: return None
    labels = {x for p in pairs for x in p}
    po = sum(1 for a, b in pairs if a == b) / n
    pe = sum((sum(1 for a, _ in pairs if a == l) / n) * (sum(1 for _, b in pairs if b == l) / n) for l in labels)
    return None if pe == 1 else (po - pe) / (1 - pe)

def ece(rows, bins=10):
    if not rows: return None
    total, n = 0.0, len(rows)
    for b in range(bins):
        lo, hi = b / bins, (b + 1) / bins
        inb = [r for r in rows if lo < r["confidence"] <= hi or (b == 0 and r["confidence"] == 0)]
        if inb: total += len(inb) / n * abs(sum(r["hit"] for r in inb) / len(inb) - sum(r["confidence"] for r in inb) / len(inb))
    return total

def curve(rows):
    out = {}
    for t in [0.5, 0.6, 0.7, 0.8, 0.9, 0.95, 0.99]:
        cov = [r for r in rows if r["confidence"] >= t]
        out[str(t)] = {"coverage": len(cov) / len(rows), "accuracy": (sum(r["hit"] for r in cov) / len(cov)) if cov else None}
    return out

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--backend", choices=["jev", "llama"], required=True)
    ap.add_argument("--url"); ap.add_argument("--temperature-file"); ap.add_argument("--tag", default="")
    ap.add_argument("--score-only", action="store_true", help="rescore the existing answers file against current gold; no calls")
    ap.add_argument("items"); ap.add_argument("rubric")
    a = ap.parse_args()
    items = [json.loads(l) for l in open(a.items) if l.strip()]
    rubric_all = json.load(open(a.rubric))
    descriptions = rubric_all.pop("windows")
    rubric = rubric_all
    temps = json.load(open(a.temperature_file)) if a.temperature_file else {}
    url = a.url or (JEV_URL if a.backend == "jev" else LLAMA_URL)
    stem = f"{a.backend}{'-' + a.tag if a.tag else ''}-{os.path.splitext(os.path.basename(a.rubric))[0]}"
    os.makedirs(os.path.join(os.path.dirname(a.items), "results"), exist_ok=True)
    out_path = os.path.join(os.path.dirname(a.items), "results", stem)

    answers, scored, gate_counts, token_sizes = [], defaultdict(list), defaultdict(int), []
    t0 = time.time()
    prior = {r["id"]: r for r in (json.loads(l) for l in open(out_path + ".answers.jsonl"))} if a.score_only else {}
    for it in items:
        if a.score_only:
            row = prior.get(it["id"])
            if not row: continue
            answers.append(row); token_sizes.append(row["input_tokens"])
            for q, g in row["gated"].items(): gate_counts[q] += 1
            for q, r in row["answers"].items():
                if q in it["gold"]:
                    scored[q].append({"gold": it["gold"][q], "label": r["label"], "confidence": r["confidence"], "hit": int(it["gold"][q] == r["label"])})
            continue
        w = it["windows"]; live = {}; gates = {}
        for q, spec in rubric.items():
            g = gated(spec, w)
            if g: gates[q] = g; gate_counts[q] += 1
            else: live[q] = spec
        res, tokens = ({}, 0)
        if live:
            res, tokens = run_jev(url, w, live, descriptions) if a.backend == "jev" else run_llama(url, w, live, temps, descriptions)
            token_sizes.append(tokens)
        row = {"id": it["id"], "answers": res, "gated": gates, "input_tokens": tokens}
        answers.append(row)
        for q, r in res.items():
            if q in it["gold"]:
                scored[q].append({"gold": it["gold"][q], "label": r["label"], "confidence": r["confidence"], "hit": int(it["gold"][q] == r["label"])})
        print(f"{it['id']}: " + ", ".join(f"{q}={r['label']}@{r['confidence']:.2f}" for q, r in res.items()) + (f"  gated: {gates}" if gates else ""), file=sys.stderr)
    with open(out_path + ".answers.jsonl", "w") as f:
        for row in answers: f.write(json.dumps(row) + "\n")
    metrics = {"backend": a.backend, "url": url, "rubric": a.rubric, "items": len(items), "seconds": round(time.time() - t0, 1),
               "input_tokens": {"total": sum(token_sizes), "max_per_item": max(token_sizes, default=0)},
               "gated": dict(gate_counts), "questions": {}}
    for q, rows in scored.items():
        metrics["questions"][q] = {"n": len(rows), "accuracy": sum(r["hit"] for r in rows) / len(rows),
                                   "kappa": kappa([(r["gold"], r["label"]) for r in rows]), "ece": ece(rows), "by_threshold": curve(rows)}
    json.dump(metrics, open(out_path + ".metrics.json", "w"), indent=1)
    print(json.dumps(metrics, indent=1))

if __name__ == "__main__": main()
