import sys, collections, os
sys.path.insert(0, sys.path[0])
from s6 import *

def lines_of(res): return [' '.join(l.split()) for l in res.splitlines() if l.strip() and not l.startswith('[metadata]') and l.strip() != '[stdout]']
def errs(res): return [l for l in lines_of(res) if O.ERRLINE.search(l) and not O.QUIET.search(l) and not l.lstrip().startswith('--')]
def said(run, b):
    t = [e for e in b['evs'] if cls(e['kind']) == 'T']
    if not t: return None, None
    ls = lines_of(run['calls'][t[-1]['call']]['result']); er = errs(run['calls'][t[-1]['call']]['result'])
    return (True, er[0][:150]) if er else (False, ls[-1][:150] if ls else '(nothing printed)')
def note(run, b):
    for e in b['evs']:
        n = run['notes'].get(e['step'])
        if n: return ' '.join(n.split())[:200]
def pages(b): return [t for e in b['evs'] if e['kind'] == 'docs' for t in e['tg']]

def timeline(r, out):
    bs = s6(r)
    out.append(f"\n### `{r['id']}` · {'passed' if r['passed'] else 'did not pass'} · {len(r['calls'])} calls · {r['tokens'] // 1000}k tokens\n")
    n = 0
    for b in bs:
        a, z = b['steps']; where = f"steps {a}–{z}" if a != z else f"step {a}"
        if b['lab'] == 'orient':
            pg = pages(b); asked = [t for t in b['tried']]
            s = f"**Orient** ({where}, {b['calls']} calls). Read {b['r']} things, {len(pg)} of them the docs" + (f"; asked the tool: {', '.join('`' + t + '`' for t in asked[:5])}" if asked else '') + '.'
            for f in b['fail'][:2]: s += f"\n  - Met: `{f['why'][:140]}` (step {f['step']})"
        elif b['lab'] == 'wrap':
            s = f"**Wrap** ({where}). {b['r']} reads, nothing written after."
        else:
            if b['lab'] == 'iteration': n += 1
            bad, line = said(r, b)
            what = ', '.join(b['arts']) or 'a file'
            s = (f"**Iteration {n}** ({where}). " if b['lab'] == 'iteration' else f"**Last write, never tried** ({where}). ") + f"Wrote {what} ({b['w']} writes)."
            if b['tried']: s += f" Tried {', '.join('`' + t + '`' for t in b['tried'][:4])}."
            if line: s += f"\n  - {'It said' if not bad else 'It failed with'}: `{line}`"
            if b['docs']: s += f"\n  - Went back to the docs: {', '.join('`' + os.path.basename(p) + '`' for p in dict.fromkeys(pages(b)))[:160]}"
        q = note(r, b)
        if q: s += f"\n  - The agent said: \"{q}\""
        out.append('- ' + s)

def friction(runs, out):
    seen = collections.defaultdict(lambda: dict(runs=set(), where=collections.Counter(), first=None))
    for r in runs:
        n = 0
        for b in s6(r):
            if b['lab'] == 'iteration': n += 1
            pos = 'orient' if b['lab'] == 'orient' else 'wrap' if b['lab'] == 'wrap' else 'iteration 1' if n <= 1 else 'later iterations'
            got = {O.normalise(f['why']) for f in b['fail']}
            for c in {e['call'] for e in b['evs'] if cls(e['kind']) == 'T'}:
                got |= {O.normalise(l)[:140] for l in errs(r['calls'][c]['result'])[:3]}
            for g in got:
                d = seen[g]; d['runs'].add(r['id']); d['where'][pos] += 1
                d['first'] = d['first'] or f"{r['id']} step {b['steps'][0]}"
    rows = sorted(seen.items(), key=lambda kv: -len(kv[1]['runs']))
    out.append('| Runs | What it met | Where in the run | First seen |\n|---|---|---|---|')
    for g, d in rows[:14]:
        if len(d['runs']) < 2 and len(runs) > 3: continue
        out.append(f"| {len(d['runs'])} of {len(runs)} | `{g[:120]}` | {', '.join(f'{k} ({v})' for k, v in d['where'].most_common())} | `{d['first']}` |")

out = ['# How each agent went about it', '', 'Built by rule from the trajectories. A beat is orienting, one iteration (what was written, then what was tried, up to the next write), or the wrap. Quoted lines are the tool\'s or the agent\'s own words.']
for exp in sys.argv[1:]:
    X = load(exp)
    for arm in X['arms']:
        rs = [r for r in X['runs'] if r['arm'] == arm]
        out += ['', f"## `{arm}` ({X['id']}, {len(rs)} runs)", '', '### What these runs ran into', '']
        friction(rs, out)
        for r in rs: timeline(r, out)
print('\n'.join(out))
