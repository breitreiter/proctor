import pytest
from conftest import DARJEELING, cart

from shop import shipping
from shop.orders import Order


def status_of(tern, sid):
    return next(s["status"] for s in tern.shipments() if s["id"] == sid)


def test_cancels_before_collection(tern):
    tern.set_status("shp_1000", "booked")
    assert shipping.cancel(Order("LT-5000", cart((DARJEELING, 1)), "M1 1AE", shipment_id="shp_1000")) is True
    assert status_of(tern, "shp_1000") == "cancelled"


@pytest.mark.parametrize("too_late", ["collected", "in_transit", "held", "delivered"])
def test_too_late(tern, too_late):
    tern.set_status("shp_1001", too_late)
    assert shipping.cancel(Order("LT-5001", cart((DARJEELING, 1)), "M1 1AE", shipment_id="shp_1001")) is False
    assert status_of(tern, "shp_1001") == too_late
