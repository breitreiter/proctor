"""Shipping with Tern, our carrier. Use shop.tern.call for requests; shop.settings has the warehouse postcode."""
from datetime import date

from shop.cart import Cart
from shop.orders import Order


def quote_at_checkout(cart: Cart, postcode: str) -> int:
    """The cheapest price Tern offers to ship this cart from the warehouse to postcode, in pence."""
    raise NotImplementedError


def quote_options(cart: Cart, postcode: str) -> list[tuple[str, int, int]]:
    """Every service Tern offers for this cart and postcode, as (service, price in pence, days to deliver),
    cheapest first."""
    raise NotImplementedError


def book(order: Order, service: str) -> str:
    """Book the order's parcel with Tern on the given service and return Tern's shipment id."""
    raise NotImplementedError


def shipments_since(day: date) -> list[dict]:
    """Every Tern shipment created on or after day, as Tern describes them."""
    raise NotImplementedError


def order_status(shipment_id: str) -> str:
    """The shop's status (one of shop.orders.STATUSES) for the order whose Tern shipment this is."""
    raise NotImplementedError


def cancel(order: Order) -> bool:
    """Cancel the order's Tern shipment. True if Tern cancelled it; False if it is too late to cancel."""
    raise NotImplementedError
