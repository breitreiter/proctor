# Compiled by auspex into a task whose agent reaches a port of the machine that runs auspex. It runs as a service in
# the agent's network namespace and serves each port at 127.0.0.1, passing every connection to the unix socket of that
# name in the mounted directory, whose other end auspex holds on the host. Bytes only: it reads nothing it carries.
import socket, sys, threading

def pump(a, b):
    try:
        while (d := a.recv(65536)): b.sendall(d)
    except OSError: pass
    finally:
        try: b.shutdown(socket.SHUT_WR)
        except OSError: pass

def carry(c, path):
    u = socket.socket(socket.AF_UNIX)
    try: u.connect(path)
    except OSError as e:
        print(f"{path}: {e}", flush=True); c.close(); return
    back = threading.Thread(target=pump, args=(u, c), daemon=True); back.start()
    pump(c, u); back.join(); c.close(); u.close()

def serve(port, path):
    s = socket.socket(); s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    s.bind(("127.0.0.1", port)); s.listen(64)
    print(f"127.0.0.1:{port} -> {path}", flush=True)
    while True:
        c, _ = s.accept()
        threading.Thread(target=carry, args=(c, path), daemon=True).start()

directory, ports = sys.argv[1], [int(p) for p in sys.argv[2:]]
for port in ports[1:]: threading.Thread(target=serve, args=(port, f"{directory}/{port}.sock"), daemon=True).start()
serve(ports[0], f"{directory}/{ports[0]}.sock")
