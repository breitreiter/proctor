"""Tern's documentation site: the variant's bundle, served read-only from /srv/docs (environment/bundle/ in the task).

A path is a page: / is index.html, /a/b is a/b.html or a/b/index.html. No directory listings, so a reader finds
pages by following links, as on a real site. A variant with no bundle gets an empty directory and a 404 for every page.
Each request is printed as one JSON line.
"""
import json
import os
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import unquote, urlsplit

ROOT = Path(os.environ.get("DOCS_ROOT", "/srv/docs")).resolve()
PORT = int(os.environ.get("DOCS_PORT", "8080"))
NOT_FOUND = b"<!doctype html><title>Not found</title><h1>404</h1><p>No such page.</p>\n"


def resolve(path):
    rel = unquote(urlsplit(path).path).strip("/")
    for candidate in ([rel] if rel.endswith(".html") else [f"{rel}.html", f"{rel}/index.html"] if rel else ["index.html"]):
        page = (ROOT / candidate).resolve()
        if page.is_file() and page.is_relative_to(ROOT):
            return page
    return None


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def do_GET(self):
        page = resolve(self.path)
        data, code = (page.read_bytes(), 200) if page else (NOT_FOUND, 404)
        self.send_response(code)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        if self.command == "GET":
            self.wfile.write(data)
        print(json.dumps({"path": self.path, "status": code}), flush=True)

    do_HEAD = do_GET


if __name__ == "__main__":
    print(f"docs on :{PORT} from {ROOT}", file=sys.stderr, flush=True)
    ThreadingHTTPServer(("127.0.0.1", PORT), Handler).serve_forever()
