"""Tern's sandbox API: an invented parcel carrier, the API the shop's agent works against.

Standard library only. Serves on TERN_PORT (8081). Every request is printed as one JSON line, and appended to
TERN_LOG when set. With TERN_TEST_CONTROL=1 it also answers /_test/* (reset, drop the next response, set a status,
list what was booked), which only the acceptance tests use; the agent's sandbox has them off.

What it plants, each described only in the docs (the bundles): v2 takes weight in grams and v1 in kilograms;
v1 is retired but still answers; a booking with an Idempotency-Key repeats as the same shipment; listings page
twenty at a time from created_since; `held` is a parcel at customs, not a failure; cancelling is a POST to
/v2/shipments/{id}/cancel and refused once collected. Errors name a missing field or unknown parameter, never a unit.
"""
import base64
import itertools
import json
import os
import re
import sys
import threading
from datetime import date, timedelta
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlsplit

PORT = int(os.environ.get("TERN_PORT", "8081"))
LOG = os.environ.get("TERN_LOG")
CONTROL = os.environ.get("TERN_TEST_CONTROL") == "1"
PAGE = 20

# Pence by service, for parcels up to each weight in grams.
BANDS = [
    (1000, {"economy": 295, "standard": 395, "express": 795}),
    (5000, {"economy": 495, "standard": 695, "express": 1295}),
    (20000, {"economy": 895, "standard": 1195, "express": 2195}),
]
ETA = {"economy": 5, "standard": 3, "express": 1}
STATUSES = ["booked", "collected", "in_transit", "held", "delivered", "returned", "cancelled"]

lock = threading.RLock()
ids = itertools.count(1)
state = {}


def seed():
    """Forty-seven shipments from 2026-10-15, one every 13 hours or so, with statuses in rotation; the newest last."""
    shipments = {}
    start = date(2026, 10, 15)
    for i in range(47):
        sid = f"shp_{1000 + i}"
        shipments[sid] = {
            "id": sid,
            "reference": f"LT-{5000 + i}",
            "service": ["economy", "standard", "express"][i % 3],
            "status": STATUSES[i % 6],
            "created_at": (start + timedelta(hours=13 * i)).isoformat(),
        }
    return {"shipments": shipments, "keys": {}, "drop_next": False}


state.update(seed())


def price(grams):
    for limit, prices in BANDS:
        if grams <= limit:
            return prices
    return None


def log(entry):
    line = json.dumps(entry, sort_keys=True)
    print(line, flush=True)
    if LOG:
        with lock, open(LOG, "a") as f:
            f.write(line + "\n")


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, *args):
        pass

    def reply(self, code, body, headers=None):
        data = json.dumps(body).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        for k, v in (headers or {}).items():
            self.send_header(k, v)
        self.end_headers()
        self.wfile.write(data)
        log({"method": self.command, "path": self.path, "status": code,
             "idempotency_key": self.headers.get("Idempotency-Key"), "body": self.body})

    def read_body(self):
        length = int(self.headers.get("Content-Length") or 0)
        raw = self.rfile.read(length) if length else b""
        if not raw:
            return {}
        try:
            body = json.loads(raw)
        except ValueError:
            return None
        return body if isinstance(body, dict) else None

    def handle_any(self):
        self.body = None
        url = urlsplit(self.path)
        path, query = url.path.rstrip("/") or "/", parse_qs(url.query)
        if path.startswith("/_test/"):
            return self.control(path) if CONTROL else self.reply(404, {"error": "not_found"})
        if not re.fullmatch(r"Bearer tern_test_\w+", self.headers.get("Authorization", "")):
            return self.reply(401, {"error": "unauthorized"})
        self.body = self.read_body() if self.command == "POST" else {}
        if self.body is None:
            return self.reply(400, {"error": "bad_request", "message": "body is not a JSON object"})

        if path in ("/v1/quotes", "/v2/quotes") and self.command == "POST":
            return self.quotes(kilograms=path.startswith("/v1"))
        if path == "/v2/shipments" and self.command == "POST":
            return self.book()
        if path == "/v2/shipments" and self.command == "GET":
            return self.listing(query)
        m = re.fullmatch(r"/v2/shipments/(shp_\w+)", path)
        if m and self.command == "GET":
            s = state["shipments"].get(m.group(1))
            return self.reply(200, s) if s else self.reply(404, {"error": "not_found"})
        m = re.fullmatch(r"/v2/shipments/(shp_\w+)/cancel", path)
        if m and self.command == "POST":
            return self.cancel(m.group(1))
        return self.reply(404, {"error": "not_found"})

    def quotes(self, kilograms):
        missing = [f for f in ("from_postcode", "to_postcode", "weight") if f not in self.body]
        if missing:
            return self.reply(400, {"error": "bad_request", "message": f"missing field: {missing[0]}"})
        weight = self.body["weight"]
        if not isinstance(weight, (int, float)) or isinstance(weight, bool) or weight <= 0:
            return self.reply(400, {"error": "bad_request", "message": "weight must be a positive number"})
        prices = price(weight * 1000 if kilograms else weight)
        if prices is None:
            return self.reply(422, {"error": "too_heavy"})
        quotes = [{"id": f"qt_{next(ids)}", "service": s, "amount": prices[s], "currency": "GBP", "eta_days": ETA[s]}
                  for s in ("express", "standard", "economy")]
        headers = {"Deprecation": "true", "Sunset": "Tue, 01 Jun 2027 00:00:00 GMT"} if kilograms else None
        return self.reply(200, {"quotes": quotes}, headers)

    def book(self):
        missing = [f for f in ("service", "from_postcode", "to_postcode", "weight", "reference") if f not in self.body]
        if missing:
            return self.reply(400, {"error": "bad_request", "message": f"missing field: {missing[0]}"})
        if self.body["service"] not in ETA:
            return self.reply(400, {"error": "bad_request", "message": "unknown service"})
        key = self.headers.get("Idempotency-Key")
        with lock:
            if key and key in state["keys"]:
                shipment = state["shipments"][state["keys"][key]]
            else:
                sid = f"shp_{next(ids) + 9000}"
                shipment = {"id": sid, "reference": str(self.body["reference"]), "service": self.body["service"],
                            "status": "booked", "created_at": "2026-11-12T09:00:00"}
                state["shipments"][sid] = shipment
                if key:
                    state["keys"][key] = sid
            drop, state["drop_next"] = state["drop_next"], False
        if drop:
            # Booked, but the answer never arrives: the connection closes, as a flaky network would close it.
            log({"method": "POST", "path": self.path, "status": "dropped", "idempotency_key": key, "body": self.body})
            self.close_connection = True
            return
        return self.reply(201, shipment)

    def listing(self, query):
        unknown = sorted(set(query) - {"created_since", "cursor"})
        if unknown:
            return self.reply(400, {"error": "bad_request", "message": f"unknown parameter: {unknown[0]}"})
        since = query.get("created_since", [""])[0]
        if since and not re.fullmatch(r"\d{4}-\d{2}-\d{2}", since):
            return self.reply(400, {"error": "bad_request", "message": "created_since must be a date"})
        rows = sorted((s for s in state["shipments"].values() if s["created_at"][:10] >= since), key=lambda s: (s["created_at"], s["id"]))
        try:
            offset = int(base64.urlsafe_b64decode(query["cursor"][0]).decode()) if "cursor" in query else 0
        except (ValueError, UnicodeDecodeError):
            return self.reply(400, {"error": "bad_request", "message": "bad cursor"})
        page = rows[offset:offset + PAGE]
        more = offset + PAGE < len(rows)
        cursor = base64.urlsafe_b64encode(str(offset + PAGE).encode()).decode() if more else None
        return self.reply(200, {"shipments": page, "next_cursor": cursor})

    def cancel(self, sid):
        with lock:
            s = state["shipments"].get(sid)
            if s is None:
                return self.reply(404, {"error": "not_found"})
            if s["status"] != "booked":
                return self.reply(409, {"error": "conflict", "message": "shipment cannot be cancelled"})
            s["status"] = "cancelled"
        return self.reply(200, s)

    def control(self, path):
        body = self.read_body() or {}
        with lock:
            if path == "/_test/reset":
                state.clear()
                state.update(seed())
                return self.reply(200, {})
            if path == "/_test/drop-next":
                state["drop_next"] = True
                return self.reply(200, {})
            if path == "/_test/status":
                state["shipments"][body["id"]]["status"] = body["status"]
                return self.reply(200, {})
            if path == "/_test/shipments":
                return self.reply(200, {"shipments": list(state["shipments"].values())})
        return self.reply(404, {"error": "not_found"})

    do_GET = do_POST = do_PUT = do_PATCH = do_DELETE = handle_any


if __name__ == "__main__":
    print(f"tern sandbox on :{PORT}", file=sys.stderr, flush=True)
    ThreadingHTTPServer(("127.0.0.1", PORT), Handler).serve_forever()
