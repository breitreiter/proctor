from shop.cart import Cart
from shop.orders import PAID, STATUSES, Order


def test_a_new_order_is_paid_and_unbooked():
    order = Order("LT-1", Cart(), "M1 1AE")
    assert order.status == PAID
    assert order.shipment_id is None


def test_statuses_are_distinct():
    assert len(set(STATUSES)) == len(STATUSES)
