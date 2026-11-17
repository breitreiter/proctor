#!/usr/bin/env python3
"""Move attempts/ (the layout before the bank, project/plans/bank.md) to bank/, log/ and exams-seen/.

  usage: project/migrate/attempts-to-bank.py [root] [--move]

Without --move it prints what it would do and changes nothing. With it, for each attempts/<id>/batch.json:
  attempts/<id>/<variant>/<task>/<n>/        ->  bank/<exam>/<variant>/<task>/<n>/   (manifest "batch" becomes "log")
  attempts/<id>/batch.json                   ->  log/<id>/entry.json, less exam_def
  its exam_def                               ->  exams-seen/sha256-<hash>.json, unless that hash is already kept
  attempts/<id>/status.json, harbor/         ->  log/<id>/
  attempts/<id>/<variant>/hooks/variant.*    ->  log/<id>/hooks/<variant>.*
It stops before moving anything if an attempt's place in the bank is taken. Anything else left under attempts/<id>/
is reported and left there.
"""
import json, os, shutil, sys

args = [a for a in sys.argv[1:] if a != "--move"]
move = "--move" in sys.argv
root = os.path.abspath(args[0] if args else ".")
old = os.path.join(root, "attempts")
if not os.path.isdir(old):
    sys.exit(f"no attempts/ under {root}")

plan, taken = [], []
for run in sorted(os.listdir(old)):
    src = os.path.join(old, run)
    file = os.path.join(src, "batch.json")
    if not os.path.isfile(file):
        print(f"skipped attempts/{run}: no batch.json")
        continue
    batch = json.load(open(file))
    if "attempts" not in batch:
        print(f"skipped attempts/{run}: written before a run listed its attempts")
        continue
    moves = []
    for a in batch["attempts"]:
        rel = os.path.join(a["variant"], a["task"], str(a["attempt"]))
        frm, to = os.path.join(src, rel), os.path.join(root, "bank", batch["exam"], rel)
        if not os.path.isdir(frm):
            continue   # never started
        if os.path.exists(to):
            taken.append(os.path.relpath(to, root))
        moves.append((frm, to))
    plan.append((run, src, batch, moves))

if taken:
    sys.exit("already in the bank, nothing moved:\n  " + "\n  ".join(taken))

for run, src, batch, moves in plan:
    entry_dir = os.path.join(root, "log", run)
    digest = batch["exam_hash"].removeprefix("sha256:")[:12]
    seen = os.path.join(root, "exams-seen", f"sha256-{digest}.json")
    print(f"attempts/{run}: {len(moves)} of {len(batch['attempts'])} attempts to bank/{batch['exam']}/, entry to log/{run}/"
          + ("" if os.path.exists(seen) else f", exam to exams-seen/{os.path.basename(seen)}"))
    if not move:
        continue
    os.makedirs(entry_dir, exist_ok=True)
    if not os.path.exists(seen):
        os.makedirs(os.path.dirname(seen), exist_ok=True)
        with open(seen, "w") as f:
            json.dump({"exam": batch["exam"], "hash": batch["exam_hash"], "def": batch["exam_def"]}, f, indent=2)
            f.write("\n")
    with open(os.path.join(entry_dir, "entry.json"), "w") as f:
        json.dump({k: v for k, v in batch.items() if k != "exam_def"}, f, indent=2)
        f.write("\n")
    os.remove(os.path.join(src, "batch.json"))
    for name in ("status.json", "harbor"):
        if os.path.exists(os.path.join(src, name)):
            shutil.move(os.path.join(src, name), os.path.join(entry_dir, name))
    for frm, to in moves:
        os.makedirs(os.path.dirname(to), exist_ok=True)
        shutil.move(frm, to)
        manifest = os.path.join(to, "manifest.json")
        if os.path.isfile(manifest):
            m = json.load(open(manifest))
            m = {("log" if k == "batch" else k): v for k, v in m.items()}
            with open(manifest, "w") as f:
                json.dump(m, f, indent=2)
                f.write("\n")
    for variant in sorted(os.listdir(src)):
        hooks = os.path.join(src, variant, "hooks")
        if os.path.isdir(hooks):
            os.makedirs(os.path.join(entry_dir, "hooks"), exist_ok=True)
            for name in os.listdir(hooks):
                shutil.move(os.path.join(hooks, name), os.path.join(entry_dir, "hooks", name.replace("variant.", variant + ".", 1)))
    for d, _, _ in os.walk(src, topdown=False):
        if not os.listdir(d):
            os.rmdir(d)
    if os.path.isdir(src):
        print(f"  left under attempts/{run}: {', '.join(sorted(os.listdir(src)))}")

if move and os.path.isdir(old) and not os.listdir(old):
    os.rmdir(old)
if not move:
    print("nothing changed; run again with --move")
