import sys, os, re, json, collections, statistics
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))
import observe as O

READ = {'explore', 'docs', 'web', 'probe', 'review', 'baseline'}
WRITE = {'edit', 'fix', 'install'}
TRY = {'run', 'script', 'service', 'verify', 'other'}

def mode(kinds):
    ks = set(kinds) - set(O.TRANSPARENT)
    w, t = bool(ks & WRITE), bool(ks & TRY)
    return 'W' if w and not t else 'T' if t and not w else 'I' if w and t else 'R' if ks else None

def merge(seq):
    out = []
    for lab, i in seq:
        if out and out[-1][0] == lab: out[-1][2] = i
        else: out.append([lab, i, i])
    return out

# S0: current beats
def s0(run): return [[O.token(b, True) if False else b['kind'] + ('!' if b['oc'] == 'fail' else ''), min(b['calls']), max(b['calls'])] for b in run['beats']]

# S1: one mode per call, merged
def s1(run): return merge([(mode(c['kinds']), i) for i, c in enumerate(run['calls']) if mode(c['kinds'])])

# S2: S1 with write/try alternation folded into one "iterate" beat; a read of <=2 calls inside does not break it
def s2(run):
    b = s1(run)
    lab = ['L' if x[0] in 'WTI' else 'R' for x in b]
    for i, x in enumerate(b):                     # short reads between loop beats join the loop
        if lab[i] == 'R' and 0 < i < len(b) - 1 and x[2] - x[1] < 2: lab[i] = 'L'
    out = []
    for (m, a, z), l in zip(b, lab):
        if out and out[-1][0] == l: out[-1][2] = z; out[-1][3] += m
        else: out.append([l, a, z, m])
    for o in out:
        if o[0] == 'L':
            n = len(re.findall(r'[WI]', re.sub(r'(.)\1+', r'\1', o[3])))
            o[0] = 'loop' if n > 1 else 'write' if 'W' in o[3] or 'I' in o[3] else 'try'
        else: o[0] = 'read'
    return [o[:3] for o in out]

# S3: fixed frame. orient = before first write; build = first write .. last write; wrap = after
def s3(run):
    ms = [mode(c['kinds']) for c in run['calls']]
    w = [i for i, m in enumerate(ms) if m in ('W', 'I')]
    if not w: return [['orient', 0, len(ms) - 1]]
    out = []
    if w[0] > 0: out.append(['orient', 0, w[0] - 1])
    out.append(['build', w[0], w[-1]])
    if w[-1] < len(ms) - 1: out.append(['wrap', w[-1] + 1, len(ms) - 1])
    return out

# S4: the agent's own paragraphs. A beat is the calls between two agent messages
def s4(run):
    steps_with_note = set(run['notes'])
    out, cur, seen = [], None, None
    for i, c in enumerate(run['calls']):
        m = mode(c['kinds'])
        if m is None: continue
        if cur is None or (c['step'] in steps_with_note and c['step'] != seen):
            cur = [set(), i, i]; out.append(cur)
        seen = c['step']; cur[0].add(m); cur[2] = i
    for o in out:
        s = o[0]
        o[0] = 'loop' if 'I' in s or ('W' in s and 'T' in s) else 'write' if 'W' in s else 'try' if 'T' in s else 'read'
    return merge([(o[0], None) for o in out]) and [[a, b, c] for a, b, c in ((o[0], o[1], o[2]) for o in out)]

# S5: what was being written. A beat is consecutive writes to one kind of artifact; reads and tries attach to it
def art(path):
    p = path.lower()
    for name, pat in (('tasks', r'/tasks?/|task'), ('suite', r'suite\.json'), ('program', r'\.nb$|program'), ('config', r'proctor\.json|nb\.json|fixture'),
                      ('shakedown', r'shakedown|reference/|mock|\.sh$'), ('bundle', r'bundle|bot/|system|prompt'), ('readme', r'readme|plan\.md|\.md$'),
                      ('test', r'test|spec'), ('source', r'\.(cs|py|ts|js|go|rs|java)$')):
        if re.search(pat, p): return name
    return 'file'
def s5(run):
    by = collections.defaultdict(list)
    for e in run['events']: by[e['call']].append(e)
    out = []
    for i, c in enumerate(run['calls']):
        m = mode(c['kinds'])
        if m is None: continue
        if m in ('W', 'I'):
            tg = [t for e in by[i] if e['kind'] in ('edit', 'fix') for t in e['tg'] if '()' not in t]
            arts = sorted({art(t) for t in tg}) or ['file']
            lab = 'write:' + ('many' if len(arts) > 2 else '+'.join(arts))
        else: lab = 'read' if m == 'R' else 'try'
        out.append((lab, i))
    return merge(out)

STRATS = [('S0 beats by kind and outcome (current)', s0), ('S1 one mode per call (read/write/try/both)', s1), ('S2 modes, write-try folded into a loop', s2),
          ('S3 fixed frame: orient, build, wrap', s3), ('S4 the agent\'s own paragraphs', s4), ('S5 by artifact written', s5)]

def skel(b):
    seen = []
    for x in b:
        if x[0] not in seen: seen.append(x[0])
    return tuple(seen)
def dedup(b):
    out = []
    for x in b:
        if not out or out[-1] != x[0]: out.append(x[0])
    return tuple(out)

def load(exp):
    decl = O.load_decl(exp, None) if os.path.exists(os.path.join(exp, 'observe.json')) else O.load_decl(exp, None)
    return O.load_experiment(exp, decl)

if __name__ == '__main__':
    exps = sys.argv[1:]
    for exp in exps:
        X = load(exp)
        runs = X['runs'] if isinstance(X, dict) else X
        print('\n#####', os.path.basename(exp), len(runs), 'runs')
        groups = collections.defaultdict(list)
        for r in runs:
            groups[(r['arm'], r['task'])].append(r)
        for name, f in STRATS:
            nb, ratio, full, sk = [], [], 0, 0
            for task, rs in groups.items():
                bs = [f(r) for r in rs]
                nb += [len(b) for b in bs]; ratio += [len(b) / max(1, len(r['calls'])) for b, r in zip(bs, rs)]
                full += len({dedup(b) for b in bs}); sk += len({skel(b) for b in bs})
            print(f'{name:48s} beats/run med {statistics.median(nb):5.1f}  beats/call {statistics.median(ratio):4.2f}  routes(exact) {full:2d}  routes(skeleton) {sk:2d}  of {len(runs)} runs')
