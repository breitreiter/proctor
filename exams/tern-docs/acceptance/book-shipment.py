from conftest import DARJEELING, TEAPOT, cart

from shop import shipping
from shop.orders import Order


def booked_for(tern, reference):
    return [s for s in tern.shipments() if s["reference"] == reference]


def test_books_once(tern):
    order = Order("LT-7001", cart((DARJEELING, 3), (TEAPOT, 1)), "M1 1AE")
    sid = shipping.book(order, "standard")
    booked = booked_for(tern, "LT-7001")
    assert [s["id"] for s in booked] == [sid]
    assert booked[0]["service"] == "standard"


def test_a_dropped_answer_is_retried_without_booking_twice(tern):
    order = Order("LT-7002", cart((DARJEELING, 1)), "G1 1XQ")
    tern.post("/_test/drop-next")
    sid = shipping.book(order, "express")
    booked = booked_for(tern, "LT-7002")
    assert len(booked) == 1, f"{len(booked)} shipments booked for one order"
    assert booked[0]["id"] == sid


def test_two_orders_are_two_shipments(tern):
    a = shipping.book(Order("LT-7003", cart((DARJEELING, 1)), "M1 1AE"), "economy")
    b = shipping.book(Order("LT-7004", cart((DARJEELING, 1)), "M1 1AE"), "economy")
    assert a != b
    assert len(booked_for(tern, "LT-7003")) == 1 and len(booked_for(tern, "LT-7004")) == 1
