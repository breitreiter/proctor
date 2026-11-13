"""A thin client for Tern's API: the key, JSON in and out. Which paths and fields to use is up to the caller."""
import json
import urllib.error
import urllib.parse
import urllib.request

from shop import settings


def call(method: str, path: str, body: dict | None = None, params: dict | None = None,
         headers: dict | None = None, timeout: float = 10) -> tuple[int, dict]:
    """One request to Tern. Returns (HTTP status, decoded JSON body); an error status is returned, not raised.
    A connection that fails or drops raises, as urllib does."""
    url = settings.TERN_API_URL.rstrip("/") + path
    if params:
        url += "?" + urllib.parse.urlencode(params)
    data = json.dumps(body).encode() if body is not None else None
    request = urllib.request.Request(url, data=data, method=method)
    request.add_header("Authorization", f"Bearer {settings.TERN_API_KEY}")
    request.add_header("Accept", "application/json")
    if data is not None:
        request.add_header("Content-Type", "application/json")
    for name, value in (headers or {}).items():
        request.add_header(name, value)
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            return response.status, json.loads(response.read() or b"{}")
    except urllib.error.HTTPError as e:
        raw = e.read()
        return e.code, json.loads(raw) if raw else {}
