import sys, statistics as st
sys.path.insert(0, sys.path[0])
from s6 import *
def vec(r):
    b = s6(r); it = [x for x in b if x['lab'] == 'iteration']
    o = next((x for x in b if x['lab'] == 'orient'), None)
    w = sum(x['w'] for x in b); tries = sum(x['t'] for x in b)
    return {'calls': len(r['calls']), 'orient: share of calls': round(100 * (o['calls'] if o else 0) / len(r['calls'])), 'orient: docs reads': o['docs'] if o else 0,
            'iterations': len(it), 'writes per iteration': round(w / max(1, len(it)), 1), 'iterations that went back to docs': sum(1 for x in it if x['docs']),
            'ended on an untried write': int(b[-1]['lab'] == 'write, never tried' or (len(b) > 1 and b[-2]['lab'] == 'write, never tried')),
            'ran its own suite': int(any('shakedown' in t or t in ('proctor run',) for x in b for t in x['tried'])), 'opened the docs at all': int(any(x['docs'] for x in b))}
arms = {}
for exp in sys.argv[1:]:
    for r in load(exp)['runs']: arms.setdefault(r['arm'], []).append(vec(r))
names = list(arms)
print(f"{'':36s}" + ''.join(f"{a + ' (' + str(len(arms[a])) + ')':>26s}" for a in names))
for k in next(iter(arms.values()))[0]:
    row = f"{k:36s}"
    for a in names:
        xs = [v[k] for v in arms[a]]
        row += f"{(str(sum(xs)) + ' of ' + str(len(xs))) if set(xs) <= {0, 1} else f'{st.median(xs):g}  ({min(xs):g} to {max(xs):g})':>26s}"
    print(row)
