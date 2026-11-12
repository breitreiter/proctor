import sys, statistics, collections, os
sys.path.insert(0, sys.path[0])
from strategies import *

def cls(k): return 'W' if k in WRITE else 'T' if k in TRY else 'R' if k in READ else None

def s6(run):
    """orient, then one beat per iteration (writes, then what was tried, up to the next write), then wrap. Cut on events, so a call that writes and runs is cut inside."""
    evs = [(cls(e['kind']), e) for e in run['events'] if cls(e['kind'])]
    first = next((i for i, (c, _) in enumerate(evs) if c == 'W'), None)
    if first is None: return [dict(lab='orient', evs=[e for _, e in evs])]
    beats = [dict(lab='orient', evs=[e for _, e in evs[:first]])] if first else []
    cur, tried = None, False
    for c, e in evs[first:]:
        if c == 'W' and (cur is None or tried):
            cur = dict(lab='iter', evs=[]); beats.append(cur); tried = False
        if c == 'T': tried = True
        cur['evs'].append(e)
    last = beats[-1]                               # trailing reads after the last try are the wrap
    tail = []
    while last['evs'] and cls(last['evs'][-1]['kind']) == 'R': tail.insert(0, last['evs'].pop())
    if tail: beats.append(dict(lab='wrap', evs=tail))
    for b in beats:
        ks = [cls(e['kind']) for e in b['evs']]
        b['w'], b['t'], b['r'] = ks.count('W'), ks.count('T'), ks.count('R')
        b['fail'] = [e for e in b['evs'] if e['oc'] == 'fail']
        b['docs'] = sum(1 for e in b['evs'] if e['kind'] == 'docs')
        b['arts'] = sorted({art(t) for e in b['evs'] if e['kind'] in ('edit', 'fix') for t in e['tg'] if '()' not in t})
        b['tried'] = sorted({(t.replace('cmd:', '') if e['kind'] == 'service' else e['kind'] + (':' + os.path.basename(t) if t else '')) for e in b['evs'] if cls(e['kind']) == 'T' for t in (e['tg'][:1] or [''])})
        b['steps'] = (b['evs'][0]['step'], b['evs'][-1]['step']) if b['evs'] else None
        b['calls'] = len({e['call'] for e in b['evs']})
        if b['lab'] == 'iter': b['lab'] = 'write, never tried' if not b['t'] else 'iteration'
    return beats

if __name__ == '__main__':
    show = '--show' in sys.argv
    for exp in [a for a in sys.argv[1:] if not a.startswith('--')]:
        X = load(exp)
        print('\n#####', os.path.basename(exp))
        rows = collections.defaultdict(list)
        for r in X['runs']:
            b = s6(r); r['s6'] = b
            it = [x for x in b if x['lab'] == 'iteration']
            rows[r['arm']].append(dict(beats=len(b), iters=len(it), failed=sum(1 for x in it if x['fail']), untried=sum(1 for x in b if x['lab'] == 'write, never tried'),
                                       orient=sum(x['calls'] for x in b if x['lab'] == 'orient'), back=sum(1 for x in it if x['docs']), calls=len(r['calls']), passed=r['passed']))
            if show:
                print(f"\n{r['id']}  {'passed' if r['passed'] else 'did not pass'}  {len(r['calls'])} calls  {len(b)} beats")
                n = 0
                for x in b:
                    if x['lab'] == 'iteration': n += 1
                    head = f"{x['lab']}{' ' + str(n) if x['lab'] == 'iteration' else ''}"
                    det = []
                    if x['lab'] in ('orient', 'wrap'): det.append(f"{x['r']} reads" + (f", {x['docs']} of the docs" if x['docs'] else '') + (f", asked: {', '.join(x['tried'][:4])}" if x['tried'] else ''))
                    else: det.append(f"wrote {'+'.join(x['arts']) or '?'} ({x['w']})" + (f"; tried {', '.join(x['tried'][:4])}" if x['tried'] else '') + (f"; went back to docs" if x['docs'] else ''))
                    if x['fail']: det.append('FAILED: ' + x['fail'][0]['why'][:90])
                    print(f"  steps {x['steps'][0]:>3}-{x['steps'][1]:<3} {head:20s} {'; '.join(det)}")
        print(f"{'arm':12s} runs  calls  beats  iterations  failed-iter  never-tried  orient-calls  back-to-docs   (medians; iterations pass|fail)")
        for arm, rs in rows.items():
            m = lambda k: statistics.median(x[k] for x in rs)
            p = [x['iters'] for x in rs if x['passed']]; f = [x['iters'] for x in rs if not x['passed']]
            print(f"{arm:12s} {len(rs):4d} {m('calls'):6.1f} {m('beats'):6.1f} {m('iters'):10.1f} {m('failed'):11.1f} {m('untried'):11.1f} {m('orient'):12.1f} {m('back'):12.1f}    {statistics.median(p) if p else '-'} | {statistics.median(f) if f else '-'}   range {min(x['beats'] for x in rs)}-{max(x['beats'] for x in rs)}")
