"""Shared by every task's acceptance: a client for the sandbox's test controls, reset before each test."""
import json
import os
import urllib.request

import pytest

from shop.cart import Cart, Item

DARJEELING = Item("T-DARJ-250", "Darjeeling, 250 g", 1250, 0.31)
TEAPOT = Item("P-POT-1L", "Cast iron teapot, 1 l", 4800, 1.42)
CADDY = Item("C-CADDY", "Tin caddy", 900, 0.12)
URN = Item("U-URN-10", "Tea urn, 10 l", 15900, 5.8)


def cart(*lines):
    c = Cart()
    for item, qty in lines:
        c.add(item, qty)
    return c


class Control:
    def __init__(self, base):
        self.base = base

    def post(self, path, body=None):
        req = urllib.request.Request(self.base + path, data=json.dumps(body or {}).encode(), method="POST",
                                     headers={"Content-Type": "application/json"})
        with urllib.request.urlopen(req, timeout=5) as r:
            return json.loads(r.read())

    def shipments(self):
        with urllib.request.urlopen(self.base + "/_test/shipments", timeout=5) as r:
            return json.loads(r.read())["shipments"]

    def set_status(self, sid, status):
        self.post("/_test/status", {"id": sid, "status": status})


@pytest.fixture
def tern():
    control = Control(os.environ["TERN_CONTROL_URL"])
    control.post("/_test/reset")
    return control
