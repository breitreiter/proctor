# A fixture's service beside the agent: it declares no network, so it shares the agent's. It holds three things the
# agent should not learn (a file, an environment variable, a header on its own loopback traffic) and says none of them
# to a caller. Once a second it calls itself with the header, so there is traffic to overhear.
import http.client, http.server, os, threading, time
class H(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        body = b"beside: the service answers, and says nothing it holds\n"
        self.send_response(200); self.send_header("Content-Length", str(len(body))); self.end_headers(); self.wfile.write(body)
    def log_message(self, *a): pass
def talk():
    while True:
        time.sleep(1)
        try:
            c = http.client.HTTPConnection("127.0.0.1", 9090, timeout=3); c.request("GET", "/", headers={"X-Held": "HELD-ON-THE-WIRE"}); c.getresponse().read()
        except OSError: pass
open("/held/answer.txt", "w").write("HELD-IN-A-FILE\n")
threading.Thread(target=talk, daemon=True).start()
http.server.ThreadingHTTPServer(("127.0.0.1", 9090), H).serve_forever()
