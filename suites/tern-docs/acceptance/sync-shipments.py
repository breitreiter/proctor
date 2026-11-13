from datetime import date

from shop import shipping


def expected(tern, since):
    return sorted(s["id"] for s in tern.shipments() if s["created_at"][:10] >= since.isoformat())


def test_every_page(tern):
    since = date(2026, 10, 20)
    got = shipping.shipments_since(since)
    ids = [s["id"] for s in got]
    assert len(ids) == len(set(ids)), "a shipment came back twice"
    assert sorted(ids) == expected(tern, since)
    assert len(ids) > 20


def test_as_tern_describes_them(tern):
    got = shipping.shipments_since(date(2026, 11, 5))
    assert sorted(s["id"] for s in got) == expected(tern, date(2026, 11, 5))
    assert all({"id", "reference", "status", "created_at"} <= set(s) for s in got)
