import pytest

from shop import shipping
from shop.orders import CANCELLED, DELIVERED, PROBLEM, PROCESSING, SHIPPED

EXPECTED = {
    "booked": PROCESSING,
    "collected": SHIPPED,
    "in_transit": SHIPPED,
    "held": SHIPPED,
    "delivered": DELIVERED,
    "returned": PROBLEM,
    "cancelled": CANCELLED,
}


@pytest.mark.parametrize("tern_status", list(EXPECTED))
def test_status(tern, tern_status):
    tern.set_status("shp_1000", tern_status)
    assert shipping.order_status("shp_1000") == EXPECTED[tern_status]
