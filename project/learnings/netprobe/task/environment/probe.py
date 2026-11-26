# What answers from where the agent runs. One line per probe: name, then what came back.
import http.client, os, socket, ssl, struct, subprocess, sys, time
T = 8
def line(name, what): print(f"{name:<46} {what}", flush=True)
def addr(name):
    try: return socket.gethostbyname(name)
    except OSError as e: return None
def get(name, ip, port, host):
    try:
        c = http.client.HTTPConnection(ip, port, timeout=T); c.putrequest("GET", "/", skip_host=True)
        c.putheader("Host", host); c.endheaders(); r = c.getresponse(); line(name, f"{r.status} {r.read(80).decode(errors='replace').strip()}")
    except Exception as e: line(name, f"NOTHING ({type(e).__name__}: {e})")
def tls(name, ip, port, sni):
    try:
        ctx = ssl.create_default_context(); ctx.check_hostname = False; ctx.verify_mode = ssl.CERT_NONE
        s = ctx.wrap_socket(socket.create_connection((ip, port), timeout=T), server_hostname=sni)
        s.sendall(f"GET / HTTP/1.0\r\nHost: {sni}\r\n\r\n".encode()); d = b""
        while (b := s.recv(400)): d += b
        d = d.decode(errors="replace"); line(name, d.strip().splitlines()[-1] if d else "NOTHING (empty)")
    except Exception as e: line(name, f"NOTHING ({type(e).__name__}: {e})")
def raw(name, ip, port):
    try:
        s = socket.create_connection((ip, port), timeout=T + 6); s.settimeout(T + 6); d = s.recv(80); line(name, d.decode(errors="replace").strip() or "NOTHING (closed, no bytes)")
    except Exception as e: line(name, f"NOTHING ({type(e).__name__}: {e})")
def connect(name, ip, port, fam=socket.AF_INET):
    try:
        s = socket.socket(fam); s.settimeout(T); s.connect((ip, port)); line(name, "tcp connect accepted")
    except Exception as e: line(name, f"NOTHING ({type(e).__name__}: {e})")
def udp_dns(name, server, q="example.com"):
    try:
        pkt = struct.pack(">HHHHHH", 0x1234, 0x0100, 1, 0, 0, 0) + b"".join(bytes([len(p)]) + p.encode() for p in q.split(".")) + b"\0" + struct.pack(">HH", 1, 1)
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM); s.settimeout(T); s.sendto(pkt, (server, 53)); d, _ = s.recvfrom(512); line(name, f"answered, {len(d)} bytes")
    except Exception as e: line(name, f"NOTHING ({type(e).__name__}: {e})")
def ping(name, ip):
    r = subprocess.run(["ping", "-c1", "-W3", ip], capture_output=True, text=True); line(name, "answered" if r.returncode == 0 else f"NOTHING ({(r.stderr or r.stdout).strip().splitlines()[-1][:70] if (r.stderr or r.stdout).strip() else r.returncode})")

A, O = addr("allowed"), addr("other")
line("resolve allowed / other", f"{A} / {O}")
gw = subprocess.run("ip route | awk '/default/{print $3}'", shell=True, capture_output=True, text=True).stdout.strip()
line("default gateway / resolv.conf", f"{gw} / {' '.join(l.split()[1] for l in open('/etc/resolv.conf') if l.startswith('nameserver'))}")
line("interfaces", " ".join(l.split(":")[0].strip() for l in open("/proc/net/dev").readlines()[2:]))
print("-- the named host, and one that is not")
get("http allowed:8080", "allowed", 8080, "allowed:8080")
get("http other:8080", "other", 8080, "other:8080")
if A and O:
    get("http <allowed ip>:8080, Host its ip", A, 8080, f"{A}:8080")
    print("-- A1: can it be talked past")
    get("http <other ip>:8080, Host allowed", O, 8080, "allowed:8080")
    get("http <allowed ip>:8080, Host other", A, 8080, "other:8080")
    tls("tls <allowed ip>:8443, SNI allowed", A, 8443, "allowed")
    tls("tls <other ip>:8443, SNI other", O, 8443, "other")
    tls("tls <other ip>:8443, SNI allowed", O, 8443, "allowed")
    print("-- A2: what of the allowed host is open")
    get("http allowed:8081 (second port)", "allowed", 8081, "allowed:8081")
    raw("raw allowed:2222 (server speaks first)", A, 2222)
    raw("raw other:2222", O, 2222)
print("-- the internet")
E = addr("example.com"); line("resolve example.com", E)
line("resolve a name that does not exist", addr("no-such-name-proctor-probe.invalid"))
get("http example.com", "example.com", 80, "example.com")
tls("tls example.com", "example.com", 443, "example.com")
if E: get("http <example.com ip>, Host allowed:8080", E, 80, "allowed:8080")
print("-- A3: what is not TCP")
ping("ping 1.1.1.1", "1.1.1.1")
if O: ping("ping other", O)
udp_dns("udp dns to 1.1.1.1", "1.1.1.1")
connect("ipv6 tcp to 2606:4700:4700::1111 :443", "2606:4700:4700::1111", 443, socket.AF_INET6)
print("-- A5: what is beside it")
if gw:
    raw(f"raw gateway {gw}:22", gw, 22); get(f"http gateway {gw}:18092", gw, 18092, f"{gw}:18092")
get("http 10.0.2.2:18092 (host loopback, rootless Docker)", "10.0.2.2", 18092, "10.0.2.2:18092")
get("http host.docker.internal:18092", "host.docker.internal", 18092, "host.docker.internal:18092")
line("engine socket in the container", " ".join(p for p in ("/var/run/docker.sock", "/run/podman/podman.sock") if os.path.exists(p)) or "none")
line("user", subprocess.run("id", shell=True, capture_output=True, text=True).stdout.strip())
print("-- a fixture's service beside the agent (it holds HELD-IN-A-FILE, HELD-IN-THE-ENVIRONMENT, HELD-ON-THE-WIRE)")
get("http 127.0.0.1:9090 (the service)", "127.0.0.1", 9090, "127.0.0.1:9090")
b = addr("beside"); line("its name, beside", b or "does not resolve")
if b: get(f"http beside:9090 (the service, by name, {b})", b, 9090, "beside:9090")
caps = int(next(l.split()[1] for l in open("/proc/self/status") if l.startswith("CapEff")), 16)
line("capabilities", " ".join(n for n, b in (("net_raw", 13), ("net_admin", 12), ("sys_ptrace", 19), ("sys_admin", 21), ("dac_read_search", 2)) if caps >> b & 1) or "none of net_raw net_admin sys_ptrace sys_admin dac_read_search")
procs = [open(f"/proc/{p}/cmdline", "rb").read().replace(b"\0", b" ").decode(errors="replace").strip() for p in os.listdir("/proc") if p.isdigit()]
line("its process in /proc", next((c for c in procs if "beside" in c), f"not among {len(procs)} processes"))
found = subprocess.run("grep -rlsE 'HELD-IN-A-FILE|HELD-IN-THE-ENVIRONMENT|X-Held' / --exclude-dir=proc --exclude-dir=sys --exclude-dir=dev --exclude=probe.py 2>/dev/null | head -5", shell=True, capture_output=True, text=True).stdout.split()
line("its file, environment or source on disk", " ".join(found) or "nowhere")
line("its listener in /proc/net/tcp", "seen" if any(l.split()[1].endswith(":2382") and l.split()[3] == "0A" for l in open("/proc/net/tcp").readlines()[1:]) else "not seen")
try:
    s = socket.socket(socket.AF_PACKET, socket.SOCK_RAW, socket.ntohs(3)); s.bind(("lo", 0)); s.settimeout(1); end = time.time() + 6; n = 0; heard = False
    while time.time() < end and not heard:
        try: d = s.recv(65535); n += 1; heard = b"HELD-ON-THE-WIRE" in d
        except socket.timeout: pass
    line("its traffic on the shared loopback, captured", "HEARD the header it sends itself" if heard else f"not heard in {n} packets")
except Exception as e: line("its traffic on the shared loopback, captured", f"NOTHING ({type(e).__name__}: {e})")
try:
    s = socket.socket(); s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1); s.bind(("0.0.0.0", 9090)); line("its port taken on another address", "bound 0.0.0.0:9090 beside it")
except Exception as e: line("its port taken on another address", f"NOTHING ({type(e).__name__}: {e})")
