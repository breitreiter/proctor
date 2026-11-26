# One probe target: says who it is on HTTP (two ports), TLS, and a raw port that speaks first, as ssh does.
import http.server, os, socket, ssl, threading
NAME = os.environ["NAME"]
class H(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        body = f"{NAME}:{self.server.server_port} host={self.headers.get('Host')}\n".encode()
        self.send_response(200); self.send_header("Content-Length", str(len(body))); self.end_headers(); self.wfile.write(body)
    def log_message(self, *a): pass
def http_on(port, tls=False):
    s = http.server.ThreadingHTTPServer(("0.0.0.0", port), H)
    if tls:
        c = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER); c.load_cert_chain("/certs/cert.pem", "/certs/key.pem")
        s.socket = c.wrap_socket(s.socket, server_side=True)
    threading.Thread(target=s.serve_forever, daemon=True).start()
def raw_on(port):
    s = socket.socket(); s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1); s.bind(("0.0.0.0", port)); s.listen()
    while True:
        c, _ = s.accept(); c.sendall(f"BANNER {NAME}\n".encode()); c.close()
http_on(8080); http_on(8081); http_on(8443, tls=True); raw_on(2222)
