#!/usr/bin/env python3
"""observe.py: THROWAWAY. A trial of the observe report in project/plans/lab.md (step 2).

Reads one experiment directory (cells with trajectory.json) and writes, by rule
alone, what the runs did: outcome, use of the docs, how the runs went (each
run cut into beats: orient, iterations, wrap), a timeline per run, recurring
errors, effort, runs to read first, and what it could not read.
Python 3.8+, standard library only, no network, no model. Deleted when
`proctor steps`, `events.jsonl` and `grade --try` exist.

  observe.py observe <experiment> [--decl observe.json]   writes observe.md, observe-timelines.md, observe-shape.txt
  observe.py steps   <cell> [41-67] [--width 300]         prints those steps
  observe.py count   <experiment> <regex> [--in calls|results|both]
  observe.py blind   <experiment> [--decl ...]            writes observe-blind/ for a model pass
  observe.py labels  <experiment> <labels.json or dir>    verifies quotes, writes observe-labels.md

The declaration (default <experiment>/observe.json; every key optional):

  { "docs_hosts":    ["localhost:8000", "docs.example.test"],
    "service_hosts": ["api.example.test"],
    "docs_tools":    ["mcp__docs__*", "Skill"],
    "docs_paths":    ["/opt/product/docs/*", "*/.claude/skills/*"],
    "service_commands": ["productcli"],
    "service_fail":  ["^problem:.*"],
    "service_tools": [],
    "verify":        ["^make check"],
    "run":           ["^\\./start\\.sh"],
    "strings":       ["/v2/otlp", "X-Example-Header"] }

Hosts and tools are globs matched against a URL's host[:port] or a tool name;
docs_paths are globs over the files a call read (a relative path is
resolved against a `cd` earlier in the same command); service_commands are
the first word of a shell command that is the product under test, and
service_fail are regexes whose match in its output is a failure; verify and
run are regexes over one shell command. Run once with no
declaration: the report lists every host and tool it saw.
"""
import collections, fnmatch, glob, json, os, random, re, shlex, statistics, sys
from urllib.parse import urlparse

URL = re.compile(r'https?://[^\s"\'<>\\)\]}|;,]+')
EXIT = re.compile(r'\[exit code: ([1-9]\d*)\]|Process exited with code ([1-9]\d*)|^Exit code:? ([1-9]\d*)|exited with (?:exit )?code ([1-9]\d*)', re.M)
VERIFY_FAIL = re.compile(r'error [A-Z]{2,}\d+|Failed!|^FAILED|^FAIL\b|--- FAIL|\b[1-9]\d* failed|Failed:\s+[1-9]|Traceback \(most recent call last\)|npm ERR!|SyntaxError|^panic:|Build FAILED|\b[1-9]\d* errors?\b', re.M)
VERIFY_OK = re.compile(r'Passed!|Build succeeded|\b\d+ passed|^ok\s|^PASS\b|0 Error|0 errors|found 0 vulnerabilities', re.M)
NET_FAIL = re.compile(r'curl: \(\d+\)[^\n]*|ECONNREFUSED|ENOTFOUND|getaddrinfo[^\n]*|Connection refused|Could not resolve host[^\n]*|status code [45]\d\d|\b[45]\d\d (?:Unauthorized|Forbidden|Not Found|Bad Request|Internal Server Error|Service Unavailable)')
STATUS = re.compile(r'HTTP/[\d.]+ (\d{3})|"status(?:Code|_code)?":\s*(\d{3})')
EDIT_FAIL = re.compile(r'(?i)^\s*(error|failed)|not found|no match|not unique|did not (find|match)|0 occurrences')
ERRLINE = re.compile(r'(?i)\b(error|failed|failure|exception|traceback|denied|refused|unauthorized|forbidden|not found|timed? ?out|cannot|could not|unable to|invalid|missing)\b|warning [A-Z]+\d+|HTTP/[\d.]+ [45]\d\d')
QUIET = re.compile(r'Passed!|^\s*PASS\b|\b0 (Error|Warning|error|failed|failures)|Error: \(none\)|^\s*(Error Message:|Stack Trace:)\s*$')
SKIP = set('cd echo printf which true false export set unset sleep pwd source . for do done if then fi else elif while in head tail wc sort uniq cut tr jq column less more xargs tee wait read local return exit test [ { } ( )'.split())
PREFIX = set('sudo time nohup env exec'.split())
EXPLORE = set('cat sed nl bat awk rg grep egrep find ls tree fd stat file diff strings od xxd basename dirname realpath readlink'.split())
PROBE = set('type command ps pgrep lsof ss netstat nc dig nslookup ping printenv whoami id uname date hostname df du'.split())
KINDS = ['explore', 'docs', 'web', 'install', 'edit', 'script', 'run', 'probe', 'service', 'verify', 'review', 'plan', 'wait', 'delegate', 'other']
READ, WRITE, TRY = {'explore', 'docs', 'web', 'probe', 'review', 'install'}, {'edit'}, {'run', 'script', 'service', 'verify', 'other'}
NOT_A_TRY = re.compile(r'^\s*(ls|cat|grep|find|head|tail|sed|stat|file|which): |\[error\] tool reported failure|bash: line \d+: file: command not found')
SCRATCH = ('/tmp/', '/var/tmp/', '/dev/shm/', '$TMPDIR/', '$tmp/', '$T/', '$TMP/')
HELP = re.compile(r'^\s*(-{1,2}[\w-]+[ ,=<\[]|usage:)', re.I)
TRANSPARENT = ('plan', 'wait')
LEGEND = 'o13 orienting, 13 calls · A advance (writes something new) · W rework (rewrites what it already wrote) · R repair (follows a beat that met an error) · lower case: written and never tried · ! the beat met an error · . wrap'
LABELS = ['orient', 'advance', 'rework', 'repair', 'wrap']
CUTS = ['ok', 'split', 'join']
ANOMALIES = ['none', 'wasted', 'redundant', 'workaround', 'unprompted']


# ---------- reading ----------

def txt(x):
    return x if isinstance(x, str) else json.dumps(x)

def load_json(path, default=None):
    try:
        with open(path, encoding='utf-8') as f:
            return json.load(f)
    except Exception:
        return default

def find_cells(exp):
    for p in sorted(glob.glob(os.path.join(exp, '*', '*', '*', 'trajectory.json'))):
        arm, task, sample = p.split(os.sep)[-4:-1]
        if arm != 'harbor':
            yield arm, task, sample, os.path.dirname(p)

def load_decl(exp, path):
    path = path or os.path.join(exp, 'observe.json')
    d = load_json(path, {}) if os.path.exists(path) else {}
    if path and not os.path.exists(path) and '--decl' in sys.argv:
        sys.exit('no declaration at ' + path)
    for k in ('docs_hosts', 'service_hosts', 'docs_tools', 'service_tools', 'docs_paths', 'service_commands', 'service_fail', 'verify', 'run', 'strings'):
        d.setdefault(k, [])
    d['_verify'] = [re.compile(x) for x in d['verify']]
    d['_run'] = [re.compile(x) for x in d['run']]
    d['_service_fail'] = [re.compile(x, re.M) for x in d['service_fail']]
    return d

def host_kind(url, decl):
    host = urlparse(url).netloc.lower()
    if any(fnmatch.fnmatch(host, g.lower()) for g in decl['docs_hosts']): return 'docs'
    if any(fnmatch.fnmatch(host, g.lower()) for g in decl['service_hosts']): return 'service'
    if re.match(r'(localhost|127\.|0\.0\.0\.0|\[::1\]|host\.docker\.internal)', host): return 'probe'
    return 'web'

def page(url):
    u = urlparse(url)
    return u.netloc.lower() + (u.path.rstrip('/') or '/')


# ---------- one call into events ----------

def strip_heredocs(cmd):
    return re.sub(r"<<-?\s*'?\"?(\w+)'?\"?[^\n]*\n.*?\n\1(?=\n|$)", '<<HEREDOC', cmd, flags=re.S)

FUNC = re.compile(r'(?:^|(?<=[;\n]))[ \t]*(?:function\s+)?([A-Za-z_]\w*)\s*\(\)\s*\{(.*?)[;\n]\s*\}', re.S)

def shell_functions(cmd):
    """The command without the shell functions it defines, and what a call to each does: a body that redirects writes a file."""
    cmd = strip_heredocs(cmd)
    funcs = {m.group(1): 'edit' if re.search(r'(?<![0-9&])>', m.group(2)) else 'run' for m in FUNC.finditer(cmd)}
    return FUNC.sub(' ', cmd), funcs

def segments(cmd):
    cmd = strip_heredocs(cmd).replace('\n', ' ; ')
    try:
        lx = shlex.shlex(cmd, posix=True, punctuation_chars=True)
        lx.whitespace_split = True
        toks = list(lx)
    except ValueError:
        toks = cmd.split()
    seg = []
    for t in toks:
        if t in ('&&', '||', ';', '|', '&', ';;', '|&'):
            if seg: yield seg
            seg = []
        else:
            seg.append(t)
    if seg: yield seg

def files_in(w):
    return [x for x in w[1:] if not x.startswith('-') and x not in ('<<', 'HEREDOC', '<<HEREDOC', '>', '>>', '2>', '/dev/null', '2>&1', '&1')
            and ('/' in x or '.' in x) and not x.startswith('s/') and ' ' not in x and '://' not in x]

def resolve(f, cwd):
    return f if f.startswith(('/', '~')) or not cwd else os.path.normpath(os.path.join(cwd, f))

def docs_files(files, cwd, decl):
    out = []
    for f in files:
        r = resolve(f, cwd)
        if any(fnmatch.fnmatch(r, g) or fnmatch.fnmatch(r + '/x', g) for g in decl['docs_paths']): out.append(r)
    return out

def segment_event(w, decl, unparsed, cwd=''):
    ev = segment_event_(w, decl, unparsed)
    if ev and ev[0] == 'edit' and ev[1] and all(resolve(t, cwd).startswith(SCRATCH) for t in ev[1]): return 'script', ['scratch']
    if ev and ev[0] == 'explore' and decl['docs_paths']:
        hit = docs_files(ev[1] or (['.'] if cwd else []), cwd, decl)
        if hit: return 'docs', hit
    return ev

def segment_event_(w, decl, unparsed):
    while w and (w[0] in PREFIX or re.match(r'^\w+=', w[0]) or (w[0] == 'timeout' and len(w) > 2)):
        w = w[2:] if w[0] == 'timeout' else w[1:]
    if not w: return None
    c, sub, line = os.path.basename(w[0]), (w[1] if len(w) > 1 else ''), ' '.join(w)
    redir = [w[i + 1] for i, x in enumerate(w[:-1]) if x in ('>', '>>') and w[i + 1] != '/dev/null' and not (i and w[i - 1] == '2')]
    if c in decl['service_commands']: return 'service', ['cmd:' + c + (' ' + sub if re.match(r'^[a-z][\w-]*$', sub) else '')]
    if any(r.search(line) for r in decl['_verify']): return 'verify', [c]
    if any(r.search(line) for r in decl['_run']): return 'run', [c]
    if c in ('curl', 'wget', 'http', 'https', 'xh'):
        urls = [u for x in w for u in URL.findall(x)]
        return (host_kind(urls[0], decl), urls) if urls else ('probe', [c])
    if c in ('cat', 'echo', 'printf', 'tee') and (redir or (c == 'tee' and files_in(w))):
        tgt = redir or files_in(w)
        return 'edit', tgt
    if c in SKIP: return None
    if (c == 'sed' and any(x.startswith('-i') for x in w)) or (c == 'perl' and any(re.match(r'-\w*i', x) for x in w)): return 'edit', files_in(w)[-8:]
    if c in ('mv', 'cp', 'rm', 'touch', 'chmod', 'patch', 'mkdir') or (c == 'git' and sub in ('mv', 'rm', 'apply')): return 'edit', files_in(w)[-8:]
    if c == 'git': return ('review', []) if sub in ('diff', 'status') else ('explore', []) if sub in ('ls-files', 'log', 'show', 'grep', 'branch', 'blame') else ('other', ['git ' + sub])
    if c in EXPLORE: return 'explore', files_in(w) or [x for x in w[1:] if x.startswith('/') or x == '.']
    if c in PROBE: return 'probe', [c]
    if c in ('kill', 'pkill', 'killall', 'docker', 'docker-compose', 'podman', 'flask', 'uvicorn', 'gunicorn', 'hypercorn') or w[0].startswith('./'): return 'run', [c]
    if c in ('pip', 'pip3', 'uv', 'poetry', 'pipenv', 'apt', 'apt-get', 'apk', 'brew', 'bundle', 'gem'): return 'install', [c]
    if c in ('npm', 'yarn', 'pnpm', 'bun'):
        if sub in ('install', 'i', 'add', 'ci', 'remove', 'uninstall'): return 'install', [c]
        if sub == 'test' or (sub == 'run' and len(w) > 2 and re.match(r'test|build|lint|typecheck|check', w[2])): return 'verify', [c + ' ' + (w[2] if sub == 'run' else sub)]
        return 'run', [c]
    if c in ('npx', 'bunx'): return ('verify', [sub]) if sub in ('tsc', 'jest', 'vitest', 'eslint', 'mocha', 'playwright') else ('run', [sub])
    if c in ('pytest', 'tox', 'ruff', 'mypy', 'tsc', 'jest', 'vitest', 'eslint', 'make', 'mvn', 'gradle', 'gradlew', 'rspec', 'phpunit'): return 'verify', [c]
    if c in ('python', 'python3', 'node', 'ruby', 'bash', 'sh', 'deno', 'perl'):
        rest = [x for x in w[1:] if x not in ('<<HEREDOC', 'HEREDOC')]
        if c.startswith('python') and rest[:1] == ['-m']:
            m = rest[1] if len(rest) > 1 else ''
            return ('verify', [m]) if m in ('pytest', 'unittest', 'py_compile', 'mypy', 'ruff', 'compileall') else ('install', ['pip']) if m == 'pip' else ('run', [m])
        if not rest or rest[0] in ('-c', '-e', '-', '-p') or '<<HEREDOC' in w or 'HEREDOC' in w: return 'script', [c]
        if c == 'node' and rest[0] == '--check': return 'verify', ['node --check']
        return 'run', [c]
    if c in ('go', 'cargo', 'dotnet'):
        if sub in ('build', 'test', 'vet', 'check', 'clippy', 'fmt', 'format'): return 'verify', [c + ' ' + sub]
        if sub in ('get', 'mod', 'add', 'restore', 'install', 'tool'): return 'install', [c]
        if sub == 'run': return 'run', [c]
        return 'explore', []
    if c.endswith('.sh') or '/' in w[0]: return 'run', [c]
    unparsed[c] += 1
    return 'other', [c]

def call_events(call, decl, unknown, unparsed):
    name = call.get('function_name') or ''
    n, a = name.lower(), call.get('arguments')
    if not isinstance(a, dict): a = {'input': a}
    if any(fnmatch.fnmatch(name, g) for g in decl['docs_tools']): return [('docs', ['tool:' + name])]
    if any(fnmatch.fnmatch(name, g) for g in decl['service_tools']): return [('service', ['tool:' + name])]
    cmd = a.get('command') if a.get('command') is not None else a.get('cmd')
    if isinstance(cmd, list): cmd = ' '.join(map(str, cmd))
    if isinstance(cmd, str) and any(k in n for k in ('bash', 'exec', 'shell')):
        ev, cwd = [], ''
        cmd, funcs = shell_functions(cmd)
        for w in segments(cmd):
            if w[0] == 'cd' and len(w) > 1: cwd = resolve(w[1], cwd)
            e = (funcs[w[0]], [w[0] + '()']) if w[0] in funcs else segment_event(w, decl, unparsed, cwd)
            if e: ev.append(e)
        return ev or [('probe', ['sh'])]
    if 'apply_patch' in n: return [('edit', re.findall(r'\*\*\* (?:Update|Add|Delete) File: (\S+)', txt(a.get('input', ''))))]
    if any(k in n for k in ('todo', 'plan', 'goal')): return [('plan', [])]
    if n in ('write_stdin', 'bashoutput', 'killshell', 'kill_shell', 'wait', 'taskoutput', 'taskstop'): return [('wait', [])]
    if n in ('task', 'agent') or 'subagent' in n: return [('delegate', [])]
    urls = URL.findall(json.dumps(a))
    if urls and any(k in n for k in ('fetch', 'web', 'http', 'browse', 'open', 'url')): return [(host_kind(urls[0], decl), urls[:1])]
    if 'search' in n and 'web' in n: return [('web', ['search'])]
    if any(k in n for k in ('list', 'glob', 'grep', 'search', 'find')) or n == 'ls': return [('explore', [])]
    p = a.get('file_path') or a.get('path') or a.get('notebook_path')
    if any(k in n for k in ('edit', 'write', 'replace', 'create')): return [('script', ['scratch'])] if p and p.startswith(SCRATCH) else [('edit', [p] if p else [])]
    if any(k in n for k in ('read', 'view', 'cat')):
        hit = docs_files([p], '', decl) if p else []
        return [('docs', hit)] if hit else [('explore', [p] if p else [])]
    unknown[name] += 1
    return [('other', [name])]

def line_with(text, m):
    s, e = text.rfind('\n', 0, m.start()) + 1, text.find('\n', m.end())
    return ' '.join(text[s:e if e >= 0 else len(text)].split())[:140]

def outcome(kind, tg, res, single, decl=None):
    """(ok | fail | ?, why). A compound call shares one result, so only markers of the kind are read."""
    if kind == 'verify':
        m = VERIFY_FAIL.search(res)
        if m: return 'fail', line_with(res, m)
        if VERIFY_OK.search(res): return 'ok', ''
        m = EXIT.search(res)
        return ('fail', 'exit ' + next(g for g in m.groups() if g)) if m else ('?', '')
    if kind in ('docs', 'service', 'web', 'probe') and any('://' in t or t.startswith('tool:') for t in tg):
        bad = [next(g for g in m.groups() if g) for m in STATUS.finditer(res)]
        bad = [s for s in bad if s >= '400']
        if bad:
            body = next((' '.join(l.split())[:100] for l in res.splitlines() if ERRLINE.search(l) and not re.match(r'\s*[<>]?\s*HTTP/', l)), '')
            return 'fail', 'HTTP ' + bad[0] + (': ' + body if body else '')
        m = NET_FAIL.search(res)
        if m: return 'fail', line_with(res, m)
        return ('?', 'empty result') if not res.strip() else ('ok', '')
    if kind == 'service' and any(t.startswith('cmd:') for t in tg):
        for r in (decl or {}).get('_service_fail', []):
            m = r.search(res)
            if m: return 'fail', ' '.join(m.group(0).split())[:140]
    if kind in ('explore', 'docs'): return 'ok', ''
    m = re.search(r'[^\n]*command not found[^\n]*', res)
    if m and single: return 'fail', ' '.join(m.group(0).split())[:140]
    if m and kind == 'script' and tg and tg[0] in m.group(0): return 'fail', ' '.join(m.group(0).split())[:140]
    if kind == 'edit' and single and EDIT_FAIL.search(res[:300]): return 'fail', ' '.join(res[:140].split())
    m = EXIT.search(res)
    if m and single: return 'fail', 'exit ' + next(g for g in m.groups() if g)
    return 'ok', ''

def call_text(call):
    a = call.get('arguments')
    if not isinstance(a, dict): return txt(a)
    for k in ('command', 'cmd', 'url', 'file_path', 'path', 'pattern', 'query', 'input'):
        if a.get(k) is not None: return txt(a[k])
    return json.dumps(a)


# ---------- one run ----------

def category(t):
    b, low = os.path.basename(t.strip('"\'')), t.lower()
    if re.search(r'(^|/)tests?/|test_|_test\.|\.test\.|\.spec\.|tests?\.cs$', low) or 'Test' in t: return 'tests'
    if b.lower().endswith(('.md', '.rst', '.txt')) and 'requirements' not in b.lower(): return 'docs'
    if b.lower().endswith(('.json', '.yaml', '.yml', '.toml', '.ini', '.env', '.cfg', 'proj', '.mod', '.sum', '.lock', '.props', '.sln')) or b in ('Dockerfile', 'Makefile', 'requirements.txt', '.env'): return 'config'
    return 'src'

def load_run(cell, decl, unknown, unparsed):
    t = load_json(os.path.join(cell, 'trajectory.json'))
    if not isinstance(t, dict): return None
    run = dict(cell=cell, calls=[], events=[], notes={}, empty=0, agent=(t.get('agent') or {}).get('name', '?'))
    for s in t.get('steps') or []:
        if s.get('source') != 'agent': continue
        m = s.get('message') or ''
        if isinstance(m, list): m = ' '.join(x.get('text', '') for x in m if isinstance(x, dict))
        if txt(m).strip(): run['notes'][s.get('step_id')] = txt(m).strip()
        res = {o.get('source_call_id'): txt(o.get('content') or '') for o in (s.get('observation') or {}).get('results') or []}
        for c in s.get('tool_calls') or []:
            r = res.get(c.get('tool_call_id'), '')
            evs = call_events(c, decl, unknown, unparsed)
            idx = len(run['calls'])
            if not r.strip() and evs[0][0] not in TRANSPARENT: run['empty'] += 1
            run['calls'].append(dict(step=s.get('step_id'), name=c.get('function_name'), text=call_text(c), result=r, kinds=[k for k, _ in evs]))
            for k, tg in evs:
                oc, why = outcome(k, tg, r, len(evs) == 1, decl)
                run['events'].append(dict(step=s.get('step_id'), call=idx, kind=k, tg=tg, oc=oc, why=why))
    run['beats'], run['plans'] = cut(run)
    return run

ANSI = re.compile(r'\x1b\[[0-9;]*[A-Za-z]')

def err_lines(res):
    # Colour codes go first: "\x1b[0;31m0 failed" is not "0 failed" to QUIET until they do.
    lines = [ANSI.sub('', l) for l in res.splitlines() if len(l) < 2000]
    return [' '.join(l.split())[:200] for l in lines if ERRLINE.search(l) and not QUIET.search(l) and not HELP.search(l)]

def try_name(e):
    t = e['tg'][0] if e['tg'] else e['kind']
    return t.split(':', 1)[1] if e['kind'] == 'service' and ':' in t else 'a scratch file' if t == 'scratch' else t + ' (inline)' if e['kind'] == 'script' else t

def cut(run):
    """A run as beats: orient (every call before the first write), then one iteration per write-then-try
    (the writes, then what was tried, up to the next write after a try), then the wrap (reads after the last
    write or try). A call is never split, so a result belongs to one beat."""
    by = collections.defaultdict(list)
    for e in run['events']: by[e['call']].append(e)
    beats, cur, plans = [], None, 0
    for i, c in enumerate(run['calls']):
        ks = set(c['kinds'])
        w, t = bool(ks & WRITE), bool(ks & TRY)
        if not (w or t or ks & READ):
            plans += 1
            continue
        if cur is None or (w and (cur['kind'] == 'orient' or cur['tried'])):
            cur = dict(kind='iteration' if w else 'orient', calls=[], tried=False)
            beats.append(cur)
        cur['calls'].append(i)
        cur['tried'] = cur['tried'] or t
    if beats and beats[-1]['kind'] == 'iteration':
        last, tail = beats[-1], []
        while len(last['calls']) > 1 and not set(run['calls'][last['calls'][-1]]['kinds']) & (WRITE | TRY): tail.insert(0, last['calls'].pop())
        if tail: beats.append(dict(kind='wrap', calls=tail, tried=False))
    seen, prev_met, n = set(), False, 0
    for b in beats:
        evs = [e for i in b['calls'] for e in by[i]]
        b['frm'], b['to'] = run['calls'][b['calls'][0]]['step'], run['calls'][b['calls'][-1]]['step']
        b['writes'] = [t for e in evs if e['kind'] in WRITE for t in (e['tg'] or ['?'])]
        b['nwrites'] = sum(1 for e in evs if e['kind'] in WRITE)
        b['tries'] = list(dict.fromkeys(try_name(e) for e in evs if e['kind'] in TRY))
        b['reads'] = sum(1 for e in evs if e['kind'] in READ)
        b['docs'] = [t for e in evs if e['kind'] == 'docs' for t in e['tg']]
        met = [(e['step'], e['why']) for e in evs if e['oc'] == 'fail' and e['why'] and not NOT_A_TRY.search(e['why'])]
        for i in b['calls']:
            if set(run['calls'][i]['kinds']) & TRY: met += [(run['calls'][i]['step'], l) for l in err_lines(run['calls'][i]['result']) if not NOT_A_TRY.search(l)][:6]
        b['met'] = list({normalise(l): (st, l) for st, l in reversed(met)}.values())[::-1]
        b['intent'] = None
        if b['kind'] == 'iteration':
            n += 1
            b['n'] = n
            b['intent'] = 'repair' if prev_met else 'advance' if not seen or any(t not in seen for t in b['writes']) else 'rework'
            seen.update(b['writes'])
            prev_met = bool(b['met'])
        b['label'] = b['intent'] or b['kind']
    return beats, plans

def strip_of(run):
    out = []
    for b in run['beats']:
        if b['kind'] == 'orient': out.append('o%d' % len(b['calls']))
        elif b['kind'] == 'wrap': out.append('.')
        else:
            ch = b['intent'][0].upper() if b['intent'] != 'rework' else 'W'
            out.append((ch if b['tried'] else ch.lower()) + ('!' if b['met'] else ''))
    return ' '.join(out)

def measures(run):
    bs = run['beats']
    it = [b for b in bs if b['kind'] == 'iteration']
    o = next((b for b in bs if b['kind'] == 'orient'), None)
    return dict(calls=len(run['calls']), orient=len(o['calls']) if o else 0, orient_share=100.0 * (len(o['calls']) if o else 0) / len(run['calls']),
                orient_docs=len(o['docs']) if o else 0, iterations=len(it), writes_per=sum(b['nwrites'] for b in it) / len(it) if it else None,
                met=sum(1 for b in it if b['met']), repairs=sum(1 for b in it if b['intent'] == 'repair'), back=sum(1 for b in it if b['docs']),
                untried=sum(1 for b in it if not b['tried']), ends_untried=bool(it) and not it[-1]['tried'], wrote=bool(it))

def artifacts(targets):
    names = list(dict.fromkeys(os.path.basename(t.rstrip('/')) or t for t in targets if t and t != '?'))
    return ', '.join('`%s`' % x for x in names[:5]) + (' and %d more' % (len(names) - 5) if len(names) > 5 else '') if names else 'files'

def timeline(run):
    """One run as sentences, each a template filled from a beat. Quoted lines are a tool's or the agent's own words."""
    L = ['### `%s` · %s · %d calls · %s tokens' % (run['id'], {True: 'passed', False: 'did not pass', None: 'not graded'}[run['passed']], len(run['calls']), num(run['tokens'] or None)), '']
    for b in run['beats']:
        where = 'steps %s to %s' % (b['frm'], b['to']) if b['frm'] != b['to'] else 'step %s' % b['frm']
        if b['kind'] == 'orient':
            s = '**Orient** (%s, %d calls). %d reads, %d of the docs' % (where, len(b['calls']), b['reads'], len(b['docs']))
            if b['tries']: s += '; ran %s' % ', '.join('`%s`' % t for t in b['tries'][:5])
            s += '.'
        elif b['kind'] == 'wrap':
            s = '**Wrap** (%s). %d reads, nothing written or tried after.' % (where, b['reads'])
        else:
            s = '**Iteration %d, %s** (%s). Wrote %s (%d writes).' % (b['n'], b['intent'], where, artifacts(b['writes']), b['nwrites'])
            s += ' Tried %s.' % ', '.join('`%s`' % t for t in b['tries'][:4]) if b['tried'] else ' Never tried.'
        L.append('- ' + s)
        for st, l in b['met'][:3]: L.append('  - Met at step %s: `%s`' % (st, l[:160].replace('`', "'")))
        if len(b['met']) > 3: L.append('  - and %d more error lines' % (len(b['met']) - 3))
        if b['kind'] == 'iteration' and b['docs']: L.append('  - Went back to the docs: %s' % artifacts(b['docs']))
        said = next((run['notes'][run['calls'][i]['step']] for i in b['calls'] if run['calls'][i]['step'] in run['notes']), None)
        if said: L.append('  - The agent: "%s"' % ' '.join(said.split())[:220])
    return L + ['']


# ---------- the experiment ----------

def load_experiment(exp, decl):
    exp = os.path.abspath(exp)
    meta = load_json(os.path.join(exp, 'experiment.json'), {}) or {}
    grading = (meta.get('suite_def') or {}).get('grading') or {}
    unknown, unparsed, runs, skipped = collections.Counter(), collections.Counter(), [], []
    for arm, task, sample, cell in find_cells(exp):
        run = load_run(cell, decl, unknown, unparsed)
        if run is None:
            skipped.append((arm + '/' + task + '/' + sample, 'trajectory.json unreadable')); continue
        run.update(arm=arm, task=task, sample=sample, id=arm + '/' + task + '/' + sample)
        checks = load_json(os.path.join(cell, 'checks.json'), {}) or {}
        core = load_json(os.path.join(cell, 'core.json'), {}) or {}
        run['checks'] = {k: (v or {}).get('result') for k, v in checks.items()}
        run['tokens'] = (core.get('input_tokens') or 0) + (core.get('output_tokens') or 0)
        run['counted'] = not any(run['checks'].get(c) == 'fail' for c in grading.get('validity') or [])
        run['passed'] = all(run['checks'].get(c) == 'pass' for c in grading['pass']) if grading.get('pass') and checks else None
        if not run['calls']:
            skipped.append((run['id'], 'no tool calls')); continue
        runs.append(run)
    return dict(dir=exp, id=meta.get('id') or os.path.basename(exp), runs=runs, skipped=skipped, unknown=unknown, unparsed=unparsed,
                grading=grading, arms=sorted({r['arm'] for r in runs}), tasks=sorted({r['task'] for r in runs}))

def med(xs):
    xs = [x for x in xs if x is not None]
    return statistics.median(xs) if xs else None

def num(x):
    return '' if x is None else ('%.0fk' % (x / 1000) if x >= 10000 else ('%g' % round(x, 1)))

def table(head, rows):
    if not rows: return ['(none)', '']
    out = ['| ' + ' | '.join(head) + ' |', '|' + '---|' * len(head)]
    out += ['| ' + ' | '.join(str(c).replace('|', '\\|').replace('\n', ' ') for c in r) + ' |' for r in rows]
    return out + ['']

def normalise(line):
    line = re.sub(r'HTTP/[\d.]+ (?=\d{3})', 'HTTP ', line)
    line = re.sub(r'(?:[\w.\-~]*/)+([\w.\-]+)', r'…/\1', line)
    line = re.sub(r'\b[0-9a-f]{8,}\b', 'H', line)
    parts = re.split(r'(HTTP \d{3})', ' '.join(line.split()))        # a status is kept; every other number is blanked
    return ''.join(x if i % 2 else re.sub(r'(?<![A-Za-z0-9])\d+', 'N', x) for i, x in enumerate(parts))[:140]

def of(n, d):
    return '%d of %d' % (n, d)

def observe(exp, decl):
    X = load_experiment(exp, decl)
    runs, arms, tasks = X['runs'], X['arms'], X['tasks']
    if not runs: sys.exit('no cells with tool calls under ' + X['dir'])
    by_arm = {a: [r for r in runs if r['arm'] == a] for a in arms}
    L = ['# What the runs did: `%s`' % X['id'], '',
         'Written by `project/lab/observe.py` from the trajectories alone, by rule. No model read anything. '
         'Every step number can be checked with `observe.py steps <cell> <range>`.', '',
         '%d runs with tool calls · %d arms · %d tasks · %d tool calls' % (len(runs), len(arms), len(tasks), sum(len(r['calls']) for r in runs)), '']
    declared = bool(decl['docs_hosts'] or decl['docs_tools'])

    # 1 outcome
    L += ['## Outcome', '']
    rows = []
    for a in arms:
        rs = by_arm[a]
        counted = [r for r in rs if r['counted']]
        known = [r for r in counted if r['passed'] is not None]
        rows.append(['`%s`' % a, rs[0]['agent'], len(rs), len(counted), of(sum(r['passed'] for r in known), len(known)) if known else '',
                     num(med([len(r['calls']) for r in rs])), num(med([r['tokens'] or None for r in rs]))])
    L += table(['Arm', 'Agent', 'Runs', 'Counted', 'Passed', 'Calls (median)', 'Tokens (median)'], rows)
    if not X['grading'].get('pass'): L += ['No `grading.pass` list in `experiment.json`, so no pass column.', '']

    # 2 the surface
    L += ['## Use of the docs', '']
    hosts = collections.defaultdict(lambda: [0, set(), ''])
    for r in runs:
        for e in r['events']:
            for t in e['tg']:
                if '://' in t:
                    h = hosts[urlparse(t).netloc.lower()]; h[0] += 1; h[1].add(r['id']); h[2] = e['kind']
    if not declared:
        L += ['**No docs host or tool is declared**, so nothing below is counted as docs. Hosts seen in commands and fetch tools:', '']
        L += table(['Host', 'Read as', 'Calls', 'Runs'], [['`%s`' % h, v[2], v[0], len(v[1])] for h, v in sorted(hosts.items(), key=lambda kv: -kv[1][0])][:30])
    for kind, title in (('docs', 'docs'), ('service', 'the service')):
        if not any(e['kind'] == kind for r in runs for e in r['events']): continue
        rows = []
        for a in arms:
            rs = by_arm[a]
            per = [[e for e in r['events'] if e['kind'] == kind] for r in rs]
            first = [next((i + 1 for i, c in enumerate(r['calls']) if kind in c['kinds']), None) for r in rs]
            rows.append(['`%s`' % a, len(rs), sum(1 for p in per if p), sum(1 for p in per if any(e['oc'] == 'ok' for e in p)),
                         sum(1 for p in per if any(e['oc'] == 'fail' for e in p)), num(med([len(p) for p in per])), num(med(first))])
        L += ['Calls to %s, by arm. "First call" is its position among the run\'s tool calls.' % title, '']
        L += table(['Arm', 'Runs', 'Runs that called', 'Runs with an answer', 'Runs with a failure', 'Calls per run (median)', 'First call (median)'], rows)
        why = collections.defaultdict(lambda: collections.Counter())
        for r in runs:
            for e in r['events']:
                if e['kind'] == kind and e['oc'] == 'fail': why[normalise(e['why'])][r['arm']] += 1
        if why:
            L += ['Failures calling %s, events by arm:' % title, '']
            L += table(['Failure'] + ['`%s`' % a for a in arms], [['`%s`' % w] + [c[a] or '' for a in arms] for w, c in sorted(why.items(), key=lambda kv: -sum(kv[1].values()))[:12]])
        pages = collections.defaultdict(lambda: collections.defaultdict(set))
        for r in runs:
            for e in r['events']:
                if e['kind'] == kind:
                    for t in e['tg']: pages[page(t) if '://' in t else t][r['arm']].add(r['id'])
        L += ['Runs that called each, of the arm\'s runs (%s):' % ', '.join('`%s` %d' % (a, len(by_arm[a])) for a in arms), '']
        L += table(['Page or tool'] + ['`%s`' % a for a in arms], [['`%s`' % p] + [len(v[a]) or '' for a in arms] for p, v in sorted(pages.items(), key=lambda kv: -sum(len(s) for s in kv[1].values()))[:30]])
    if decl['strings']:
        rows = []
        for s in decl['strings']:
            for where in ('a command', 'a result', 'the final diff'):
                row = ['`%s`' % s, where]
                for a in arms:
                    n = 0
                    for r in by_arm[a]:
                        if where == 'the final diff':
                            try: hit = s in open(os.path.join(r['cell'], 'diff.patch'), encoding='utf-8', errors='replace').read()
                            except OSError: hit = False
                        else: hit = any(s in (c['text'] if where == 'a command' else c['result']) for c in r['calls'])
                        n += hit
                    row.append(of(n, len(by_arm[a])))
                rows.append(row)
        L += ['Declared strings: runs where the string appears, as a literal search.', ''] + table(['String', 'In'] + ['`%s`' % a for a in arms], rows)

    # 3 how the runs went
    for r in runs: r['m'] = measures(r)
    L += ['## How the runs went', '', 'A run is cut into beats, as in a story. **Orient** is every call before the first write. Then one **iteration** per '
          'write-then-try: what was written, then what was tried, up to the next write. An iteration is an **advance** when it writes something new, '
          'a **rework** when it only rewrites what it already wrote, and a **repair** when the beat before it met an error. The **wrap** is the reads after the last write or try. '
          'A call is never split between beats. "Met an error" means a failed call, or a line that looks like an error in what a try printed.', '']
    def spread(rs, k, pct=False):
        xs = [r['m'][k] for r in rs if r['m'][k] is not None]
        if not xs: return ''
        f = (lambda x: '%.0f%%' % x) if pct else num
        return '%s (%s to %s)' % (f(statistics.median(xs)), f(min(xs)), f(max(xs)))
    rows = [['Calls'] + [spread(by_arm[a], 'calls') for a in arms],
            ['Calls spent orienting'] + [spread(by_arm[a], 'orient') for a in arms],
            ['Share of calls spent orienting'] + [spread(by_arm[a], 'orient_share', True) for a in arms],
            ['Docs reads while orienting'] + [spread(by_arm[a], 'orient_docs') for a in arms],
            ['Iterations'] + [spread(by_arm[a], 'iterations') for a in arms],
            ['Writes per iteration'] + [spread(by_arm[a], 'writes_per') for a in arms],
            ['Iterations that met an error'] + [spread(by_arm[a], 'met') for a in arms],
            ['Repairs'] + [spread(by_arm[a], 'repairs') for a in arms],
            ['Iterations that went back to the docs'] + [spread(by_arm[a], 'back') for a in arms],
            ['Runs that wrote nothing'] + [of(sum(1 for r in by_arm[a] if not r['m']['wrote']), len(by_arm[a])) for a in arms],
            ['Runs that never tried anything they wrote'] + [of(sum(1 for r in by_arm[a] if r['m']['wrote'] and r['m']['untried'] == r['m']['iterations']), len(by_arm[a])) for a in arms],
            ['Runs whose last write was never tried'] + [of(sum(1 for r in by_arm[a] if r['m']['ends_untried']), len(by_arm[a])) for a in arms]]
    L += ['Per run, by arm: the median, with the lowest and highest run.', ''] + table([''] + ['`%s` (%d runs)' % (a, len(by_arm[a])) for a in arms], rows)
    for t in tasks:
        rs = [r for r in runs if r['task'] == t]
        m = med([len(r['calls']) for r in rs])
        for r in rs:
            n = len(r['calls'])
            r['flag'] = '' if len(rs) < 4 else 'SHORT' if n <= m * 0.5 else 'LONG' if n >= m * 1.5 else ''
        L += ['### `%s`: %d runs, median %s calls' % (t, len(rs), num(m)), '', '```']
        w = max(len(r['arm']) + len(r['sample']) + 1 for r in rs)
        for r in sorted(rs, key=lambda r: (r['arm'], r['sample'])):
            s = strip_of(r)
            L.append('%-*s %-4s %4d calls %-5s %s' % (w, r['arm'] + '/' + r['sample'], {True: 'pass', False: 'no', None: ''}[r['passed']], len(r['calls']), r['flag'], s if len(s) <= 150 else s[:150] + '…'))
        L += ['```', '']
    L += [LEGEND + '.', '']

    # 4 one run in full per arm and task
    def typical(rs):
        mi, mc = med([r['m']['iterations'] for r in rs]), med([len(r['calls']) for r in rs])
        most = sum(1 for r in rs if r['passed']) * 2 >= len(rs)
        return min(rs, key=lambda r: (bool(r['passed']) != most, abs(r['m']['iterations'] - mi), abs(len(r['calls']) - mc)))
    L += ['## One run in full, per task and arm', '', 'The run nearest its group\'s median iterations and calls. Every run is in `observe-timelines.md`.', '']
    T = ['# Every run as a timeline: `%s`' % X['id'], '', 'Written by `project/lab/observe.py` by rule. Each sentence is a template filled from a beat; '
         'quoted lines are a tool\'s or the agent\'s own words.', '']
    for t in tasks:
        for a in arms:
            rs = [r for r in runs if r['task'] == t and r['arm'] == a]
            if not rs: continue
            r = typical(rs)
            r['typical'] = True
            L += timeline(r)
            for r in rs: T += timeline(r)

    # 5 recurring lines
    L += ['## What the agents ran into', '', 'Lines in tool results that look like an error or warning, with numbers and paths blanked, '
          'counted when the same line shows in two or more runs. Only results of builds, tests, scripts, installs, running programs and service calls are searched. '
          '"Where" is the beat the call was in.', '']
    seen = collections.defaultdict(lambda: dict(lines=0, calls=0, runs=collections.defaultdict(set), first=None, where=collections.Counter()))
    for r in runs:
        pos = {i: 'orienting' if b['kind'] == 'orient' else 'wrap' if b['kind'] == 'wrap' else 'iteration 1' if b['n'] == 1 else 'later iterations' for b in r['beats'] for i in b['calls']}
        for i, c in enumerate(r['calls']):
            if not any(k in ('verify', 'run', 'script', 'install', 'service', 'probe', 'other') for k in c['kinds']): continue
            got = collections.Counter(normalise(l) for l in err_lines(c['result']))
            for line, n in got.items():
                s = seen[line]; s['lines'] += n; s['calls'] += 1; s['runs'][r['arm']].add(r['id']); s['where'][pos.get(i, '')] += 1
                s['first'] = s['first'] or (r['id'], c['step'])
    rec = sorted(((k, v) for k, v in seen.items() if sum(len(s) for s in v['runs'].values()) >= 2), key=lambda kv: (-sum(len(s) for s in kv[1]['runs'].values()), -kv[1]['lines']))
    L += table(['Line', 'Lines', 'Calls'] + ['Runs `%s`' % a for a in arms] + ['Where', 'First seen'],
               [['`%s`' % k, v['lines'], v['calls']] + [len(v['runs'][a]) or '' for a in arms] + [', '.join('%s (%d)' % kv for kv in v['where'].most_common()), '`%s` step %s' % v['first']] for k, v in rec[:30]])
    if len(rec) > 30: L += ['%d more recurring lines not shown.' % (len(rec) - 30), '']

    # 6 effort
    L += ['## Where the effort went', '', 'Per run, by arm: mean tool calls of each kind, and the mean kilobytes of tool result read back. '
          'A shell call that does two things counts under each.', '']
    rows = []
    for k in KINDS:
        row, any_ = ['%s' % k], False
        for a in arms:
            rs = by_arm[a]
            calls = sum(1 for r in rs for c in r['calls'] if k in fixed_kinds(r, c)) / len(rs)
            kb = sum(len(c['result']) for r in rs for c in r['calls'] if fixed_kinds(r, c)[0] == k) / len(rs) / 1024
            any_ = any_ or calls > 0
            row.append('%.1f · %.0f KB' % (calls, kb) if calls else '')
        if any_: rows.append(row)
    rows.append(['**all**'] + ['%.1f · %.0f KB' % (sum(len(r['calls']) for r in by_arm[a]) / len(by_arm[a]), sum(len(c['result']) for r in by_arm[a] for c in r['calls']) / len(by_arm[a]) / 1024) for a in arms])
    L += table(['Kind'] + ['`%s`' % a for a in arms], rows)

    # 7 runs to read first
    L += ['## Runs to read first', '']
    rows = []
    for t in tasks:
        rs = [r for r in runs if r['task'] == t]
        m = med([len(r['calls']) for r in rs])
        picks = [('Typical', typical(rs), 'step 1')]
        picks.append(('Shortest', min(rs, key=lambda r: len(r['calls'])), 'step 1'))
        picks.append(('Longest', max(rs, key=lambda r: len(r['calls'])), 'step 1'))
        mf = max(rs, key=lambda r: r['m']['repairs'])
        f = next((b for b in mf['beats'] if b['intent'] == 'repair'), None)
        if f: picks.append(('Most repairs (%d)' % mf['m']['repairs'], mf, 'step %s: first repair' % f['frm']))
        for r in [r for r in rs if r['passed'] is False][:3]:
            lv = next((b for b in reversed(r['beats']) if b['met']), None)
            picks.append(('Did not pass', r, 'step %s: %s' % (lv['met'][-1][0], lv['met'][-1][1][:60]) if lv else 'the end'))
        nd = next((r for r in rs if any(e['kind'] == 'docs' and e['oc'] == 'fail' for e in r['events'])), None)
        if nd: picks.append(('A docs call failed', nd, 'step %s' % next(e['step'] for e in nd['events'] if e['kind'] == 'docs' and e['oc'] == 'fail')))
        done = set()
        for why, r, start in picks:
            if (r['id'], why[:4]) in done or (why in ('Shortest', 'Longest') and r['id'] in {x[0] for x in done}): continue
            done.add((r['id'], why[:4]))
            rows.append(['`%s`' % t, why, '`%s`' % r['id'], len(r['calls']), num(r['tokens'] or None), {True: 'yes', False: 'no', None: ''}[r['passed']], start])
    L += table(['Task', 'Why', 'Run', 'Calls', 'Tokens', 'Passed', 'Start at'], rows)

    # 8 what it could not read
    L += ['## What this could not read', '']
    total = sum(len(r['events']) for r in runs)
    other = sum(1 for r in runs for e in r['events'] if e['kind'] == 'other')
    blind_w = sum(1 for r in runs for b in r['beats'] for t in b['writes'] if t == '?' or t.endswith('()'))
    all_w = sum(len(b['writes']) for r in runs for b in r['beats'])
    L += ['- **%d of %d actions (%.0f%%) are "other"**: a tool or command no rule names. Over 15%% and the tables above are not to be trusted.' % (other, total, 100.0 * other / total),
          '- %d of %d writes went through a shell helper or a command whose target the rules could not name, so advance and rework are less sure there.' % (blind_w, all_w),
          '- Whether a try passed is not read. "Met an error" is a failed call or an error-looking line, and a try that fails on purpose looks the same.', '']
    rows = [['`%s`' % a, of(sum(1 for r in by_arm[a] for e in r['events'] if e['kind'] == 'other'), sum(len(r['events']) for r in by_arm[a])),
             of(sum(r['empty'] for r in by_arm[a]), sum(len(r['calls']) for r in by_arm[a])), sum(r['plans'] for r in by_arm[a])] for a in arms]
    L += table(['Arm', 'Other actions', 'Calls with an empty result', 'Planning and waiting calls'], rows)
    if X['unknown']: L += ['Tools no rule names:', ''] + table(['Tool', 'Calls'], [['`%s`' % k, v] for k, v in X['unknown'].most_common(30)])
    if X['unparsed']: L += ['Commands no rule names (first word):', ''] + table(['Command', 'Times'], [['`%s`' % k, v] for k, v in X['unparsed'].most_common(30)])
    if X['skipped']: L += ['Cells left out:', ''] + table(['Cell', 'Why'], [['`%s`' % a, b] for a, b in X['skipped']])

    # every run
    L += ['## Every run', '']
    rows = []
    for r in sorted(runs, key=lambda r: (r['task'], r['arm'], r['sample'])):
        d = [e for e in r['events'] if e['kind'] == 'docs']
        m = r['m']
        rows.append(['`%s`' % r['id'], {True: 'yes', False: 'no', None: ''}[r['passed']] if r['counted'] else 'not counted', len(r['calls']), num(r['tokens'] or None), len(r['beats']),
                     m['orient'], m['iterations'], m['met'] or '', m['repairs'] or '', m['untried'] or '',
                     '%d / %d' % (sum(1 for e in d if e['oc'] != 'fail'), sum(1 for e in d if e['oc'] == 'fail')) if d else '', '`%s`' % strip_of(r)])
    L += table(['Run', 'Passed', 'Calls', 'Tokens', 'Beats', 'Orient calls', 'Iterations', 'Met an error', 'Repairs', 'Never tried', 'Docs calls ok / failed', 'Beats, one mark each'], rows)

    with open(os.path.join(X['dir'], 'observe-timelines.md'), 'w', encoding='utf-8') as f: f.write('\n'.join(T) + '\n')
    print('wrote', os.path.join(X['dir'], 'observe-timelines.md'))
    out = os.path.join(X['dir'], 'observe.md')
    with open(out, 'w', encoding='utf-8') as f: f.write('\n'.join(L) + '\n')
    shape(X, os.path.join(X['dir'], 'observe-shape.txt'), other, total)
    print('wrote', out)
    print('wrote', os.path.join(X['dir'], 'observe-shape.txt'))
    print('%d runs, %d actions, %.0f%% other' % (len(runs), total, 100.0 * other / total))

def fixed_kinds(run, call):
    """A call's kinds as first read (edit, never fix: the effort table is by what a call touched)."""
    return call['kinds']

def shape(X, path, other, total):
    """Counts only: no command, path, result, arm id or task id. For carrying off the machine if that is allowed."""
    A = {a: 'A%d' % (i + 1) for i, a in enumerate(X['arms'])}
    T = {t: 'T%d' % (i + 1) for i, t in enumerate(X['tasks'])}
    L = ['observe.py shape: counts only', 'runs %d arms %d tasks %d actions %d other %d' % (len(X['runs']), len(A), len(T), total, other), '',
         'run agent calls beats iterations flag passed empty_results | ' + ' '.join(KINDS) + ' | strip']
    for r in X['runs']:
        k = collections.Counter(e['kind'] for e in r['events'])
        L.append('%s/%s/%s %s %d %d %d %s %s %d | %s | %s' % (A[r['arm']], T[r['task']], r['sample'], r['agent'], len(r['calls']), len(r['beats']), r['m']['iterations'],
                 r['flag'] or '-', {True: 'y', False: 'n', None: '-'}[r['passed']], r['empty'], ' '.join(str(k[x]) for x in KINDS), strip_of(r)))
    safe = lambda s: s if re.match(r'^[A-Za-z0-9_.\-]{1,24}$', s) else '(withheld)'
    L += ['', 'tools no rule names: ' + ', '.join('%s %d' % (safe(k), v) for k, v in X['unknown'].most_common(40)),
          'commands no rule names: ' + ', '.join('%s %d' % (safe(k), v) for k, v in X['unparsed'].most_common(40))]
    with open(path, 'w', encoding='utf-8') as f: f.write('\n'.join(L) + '\n')


# ---------- steps, count ----------

def steps(cell, rng, width):
    path = cell if cell.endswith('.json') else os.path.join(cell, 'trajectory.json')
    lo, hi = (int(x) for x in rng.split('-')) if rng and '-' in rng else (int(rng), int(rng)) if rng else (0, 10 ** 9)
    for s in json.load(open(path, encoding='utf-8')).get('steps') or []:
        i = s.get('step_id')
        if not isinstance(i, int) or not lo <= i <= hi: continue
        m = s.get('message') or ''
        if isinstance(m, list): m = ' '.join(x.get('text', '') for x in m if isinstance(x, dict))
        print('── step %s (%s)' % (i, s.get('source')))
        if txt(m).strip(): print('   NOTE   ' + ' '.join(txt(m).split())[:width * 2])
        res = {o.get('source_call_id'): txt(o.get('content') or '') for o in (s.get('observation') or {}).get('results') or []}
        for c in s.get('tool_calls') or []:
            print('   CALL   %s: %s' % (c.get('function_name'), ' '.join(call_text(c).split())[:width]))
            r = res.get(c.get('tool_call_id'), '')
            print('   RESULT %d bytes: %s' % (len(r), ' '.join(r.split())[:width] or '(empty)'))

def count(exp, pattern, where, decl):
    X, rx = load_experiment(exp, decl), re.compile(pattern)
    for a in X['arms']:
        rs, hits, events = [r for r in X['runs'] if r['arm'] == a], [], 0
        for r in rs:
            m = [c['step'] for c in r['calls'] if (where != 'results' and rx.search(c['text'])) or (where != 'calls' and rx.search(c['result']))]
            events += len(m)
            if m: hits.append('%s step %s' % (r['id'], m[0]))
        print('%-24s %d of %d runs, %d calls' % (a, len(hits), len(rs), events))
        for h in hits: print('    ' + h)


# ---------- the model pass: blinded beats out, verified labels in ----------

PROMPT = """You are checking how coding-agent runs were cut into beats. Each file R###.txt is one run, already cut
by rule into beats B1, B2, ..., each with the rule's label. Treat each file on its own. Write no prose anywhere.

The rule: "orient" is everything before the run first writes anything. Then one beat per iteration: what the
agent wrote, then what it tried, up to its next write. "wrap" is the reading after the last write or try.
An iteration is labelled "advance" (it writes something new), "rework" (it only rewrites what it already
wrote) or "repair" (it answers an error the run met in the beat before).

For every beat, give:
- "cut": one of %s. ok: the beat is one piece of work a person would describe in one sentence.
  split: it holds two or more separate pieces of work that a person would describe separately.
  join: it is the same piece of work as the beat before it and should not have been cut off.
- "label": one of %s. Keep the rule's label unless it is wrong.
- "quote": the agent's own statement of what it is doing, copied character for character from a NOTE inside
  that beat, at most 200 characters; "" if no NOTE in the beat states one. Never paraphrase.
- "anomaly": one of %s. wasted: the beat achieved nothing and was abandoned.
  redundant: it repeats something already done with the same result. workaround: it replaces an approach that
  failed for a reason outside the task. unprompted: it does something the task did not ask for.

Write one file, labels.json, of this shape and nothing else:
{"R001": [{"beat": 1, "cut": "ok", "label": "orient", "quote": "", "anomaly": "none"}, ...], "R002": [...]}
""" % (', '.join(CUTS), ', '.join(LABELS), ', '.join(ANOMALIES))

def key_lines(res):
    lines = [' '.join(l.split())[:200] for l in res.splitlines() if l.strip()]
    hits = [l for l in lines if ERRLINE.search(l)][:4]
    return hits or lines[:1] + (lines[-1:] if len(lines) > 1 else []) or ['(empty)']

def blind(exp, decl):
    X = load_experiment(exp, decl)
    out = os.path.join(X['dir'], 'observe-blind')
    os.makedirs(out, exist_ok=True)
    order = sorted(X['runs'], key=lambda r: r['id'])
    random.Random(7).shuffle(order)
    key = {}
    for n, r in enumerate(order, 1):
        rid = 'R%03d' % n
        key[rid] = r['id']
        L = []
        for i, c in enumerate(r['beats'], 1):
            L.append('B%d [%s] steps %s to %s' % (i, c['label'], c['frm'], c['to']))
            said = set()
            for j in sorted(c['calls']):
                call = r['calls'][j]
                if call['step'] in r['notes'] and call['step'] not in said:
                    said.add(call['step'])
                    L.append('  step %s NOTE: %s' % (call['step'], ' '.join(r['notes'][call['step']].split())[:700]))
                L.append('  step %s CALL %s: %s' % (call['step'], '+'.join(dict.fromkeys(call['kinds'])), ' '.join(call['text'].split())[:500]))
                L += ['      RESULT: ' + l for l in key_lines(call['result'])]
        with open(os.path.join(out, rid + '.txt'), 'w', encoding='utf-8') as f: f.write('\n'.join(L) + '\n')
    with open(os.path.join(out, 'PROMPT.md'), 'w', encoding='utf-8') as f: f.write(PROMPT)
    with open(os.path.join(X['dir'], 'observe-blind-key.json'), 'w', encoding='utf-8') as f: json.dump(key, f, indent=1)
    print('wrote %d runs and PROMPT.md to %s' % (len(order), out))
    print('the key is beside it, not in it: %s' % os.path.join(X['dir'], 'observe-blind-key.json'))

def labels(exp, src, decl):
    X = load_experiment(exp, decl)
    key = load_json(os.path.join(X['dir'], 'observe-blind-key.json')) or sys.exit('no observe-blind-key.json; run blind first')
    got = {}
    for p in ([src] if os.path.isfile(src) else sorted(glob.glob(os.path.join(src, '*.json')))):
        got.update(load_json(p, {}) or {})
    by_id = {r['id']: r for r in X['runs']}
    squash = lambda s: re.sub(r'\s+', '', s)
    n = quoted = verified = agree = 0
    relabel, miscut, odd, bad, cuts = collections.Counter(), [], [], collections.Counter(), collections.Counter()
    for rid, items in got.items():
        r = by_id.get(key.get(rid))
        if not r or not isinstance(items, list): bad['run not in key'] += 1; continue
        for it in items:
            i = it.get('beat') if isinstance(it, dict) else None
            if not isinstance(i, int) or not 1 <= i <= len(r['beats']): bad['no such beat'] += 1; continue
            if it.get('label') not in LABELS or it.get('anomaly') not in ANOMALIES or it.get('cut') not in CUTS: bad['label, cut or anomaly outside the fixed set'] += 1; continue
            c, q = r['beats'][i - 1], (it.get('quote') or '')
            n += 1
            notes = squash(' '.join(r['notes'].get(r['calls'][j]['step'], '') for j in c['calls']))
            if q:
                quoted += 1
                if squash(q) in notes: verified += 1
                else: bad['quote not in the beat'] += 1; q = ''
            cuts[it['cut']] += 1
            if it['label'] == c['label']: agree += 1
            else: relabel[(c['label'], it['label'])] += 1
            where = ['`%s`' % r['id'], '%s to %s' % (c['frm'], c['to'])]
            if it['cut'] != 'ok': miscut.append(where + [c['label'], it['cut'], len(c['calls']), '"%s"' % q if q else ''])
            if it['anomaly'] != 'none': odd.append(where + [it['anomaly'], '"%s"' % q if q else ''])
    L = ['# The model pass against the rules: `%s`' % X['id'], '',
         'A model said of each rule beat whether the cut is right, and gave it a label, a quote from the agent and an anomaly, each from a fixed set. '
         'A quote is kept only when it is in the beat word for word. Nothing here is the model\'s own prose.', '',
         '- %d beats read; the model called the cut right on %d (%.0f%%), wanted %d split and %d joined to the beat before.' % (n, cuts['ok'], 100.0 * cuts['ok'] / max(n, 1), cuts['split'], cuts['join']),
         '- The model kept the rule\'s label on %d (%.0f%%).' % (agree, 100.0 * agree / max(n, 1)),
         '- %d quotes offered, %d verified.' % (quoted, verified), '']
    if bad: L += ['Discarded:', ''] + table(['Why', 'Items'], [[k, v] for k, v in bad.most_common()])
    L += ['## Where the model changed the label', ''] + table(['Rule said', 'Model said', 'Beats'], [[a, b, v] for (a, b), v in relabel.most_common(20)])
    L += ['## Beats the model would cut differently', ''] + table(['Run', 'Steps', 'Rule said', 'Model wants', 'Calls', 'The agent'], miscut[:80])
    L += ['## Anomalies', ''] + table(['Run', 'Steps', 'Anomaly', 'The agent'], odd[:80])
    out = os.path.join(X['dir'], 'observe-labels.md')
    with open(out, 'w', encoding='utf-8') as f: f.write('\n'.join(L) + '\n')
    print('wrote', out)


def main(argv):
    def opt(name, default=None):
        if name in argv:
            i = argv.index(name); v = argv[i + 1]; del argv[i:i + 2]; return v
        return default
    decl_path, width, where = opt('--decl'), int(opt('--width', 300)), opt('--in', 'both')
    if len(argv) < 2 or argv[0] not in ('observe', 'steps', 'count', 'blind', 'labels'): sys.exit(__doc__)
    verb, target = argv[0], argv[1]
    if verb == 'steps': return steps(target, argv[2] if len(argv) > 2 else None, width)
    decl = load_decl(target, decl_path)
    if verb == 'observe': observe(target, decl)
    elif verb == 'count': count(target, argv[2], where, decl)
    elif verb == 'blind': blind(target, decl)
    elif verb == 'labels': labels(target, argv[2], decl)

if __name__ == '__main__':
    main(sys.argv[1:])
