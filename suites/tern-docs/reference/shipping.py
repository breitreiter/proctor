"""Shipping with Tern, our carrier. Use shop.tern.call for requests; shop.settings has the warehouse postcode.

The reference: every function written the way Tern's docs (as shipped) describe."""
import http.client
import time
import urllib.error
from datetime import date

from shop import orders, settings, tern
from shop.cart import Cart
from shop.orders import Order


class TernError(Exception):
    pass


def _grams(cart: Cart) -> int:
    return round(cart.total_weight_kg() * 1000)


def _quotes(cart: Cart, postcode: str) -> list[dict]:
    status, body = tern.call("POST", "/v2/quotes", {"from_postcode": settings.WAREHOUSE_POSTCODE,
                                                    "to_postcode": postcode, "weight": _grams(cart)})
    if status != 200:
        raise TernError(f"quote failed: {status} {body}")
    return body["quotes"]


def quote_at_checkout(cart: Cart, postcode: str) -> int:
    """The cheapest price Tern offers to ship this cart from the warehouse to postcode, in pence."""
    return min(q["amount"] for q in _quotes(cart, postcode))


def quote_options(cart: Cart, postcode: str) -> list[tuple[str, int, int]]:
    """Every service Tern offers for this cart and postcode, as (service, price in pence, days to deliver),
    cheapest first."""
    return sorted(((q["service"], q["amount"], q["eta_days"]) for q in _quotes(cart, postcode)), key=lambda o: o[1])


def book(order: Order, service: str) -> str:
    """Book the order's parcel with Tern on the given service and return Tern's shipment id."""
    body = {"service": service, "from_postcode": settings.WAREHOUSE_POSTCODE, "to_postcode": order.postcode,
            "weight": _grams(order.cart), "reference": order.id}
    for attempt in range(4):
        try:
            status, shipment = tern.call("POST", "/v2/shipments", body, headers={"Idempotency-Key": f"order-{order.id}"})
        except (urllib.error.URLError, ConnectionError, http.client.HTTPException):
            if attempt == 3:
                raise
            time.sleep(0.2 * (attempt + 1))
            continue
        if status in (200, 201):
            return shipment["id"]
        raise TernError(f"booking failed: {status} {shipment}")
    raise TernError("unreachable")


def shipments_since(day: date) -> list[dict]:
    """Every Tern shipment created on or after day, as Tern describes them."""
    shipments, params = [], {"created_since": day.isoformat()}
    while True:
        status, body = tern.call("GET", "/v2/shipments", params=params)
        if status != 200:
            raise TernError(f"listing failed: {status} {body}")
        shipments.extend(body["shipments"])
        if not body.get("next_cursor"):
            return shipments
        params = {"created_since": day.isoformat(), "cursor": body["next_cursor"]}


_STATUS = {
    "booked": orders.PROCESSING,
    "collected": orders.SHIPPED,
    "in_transit": orders.SHIPPED,
    "held": orders.SHIPPED,   # at customs: normal, clears by itself, still on its way
    "delivered": orders.DELIVERED,
    "returned": orders.PROBLEM,
    "cancelled": orders.CANCELLED,
}


def order_status(shipment_id: str) -> str:
    """The shop's status (one of shop.orders.STATUSES) for the order whose Tern shipment this is."""
    status, shipment = tern.call("GET", f"/v2/shipments/{shipment_id}")
    if status != 200:
        raise TernError(f"lookup failed: {status} {shipment}")
    return _STATUS.get(shipment["status"], orders.PROBLEM)


def cancel(order: Order) -> bool:
    """Cancel the order's Tern shipment. True if Tern cancelled it; False if it is too late to cancel."""
    status, body = tern.call("POST", f"/v2/shipments/{order.shipment_id}/cancel")
    if status == 200:
        return True
    if status == 409:
        return False
    raise TernError(f"cancel failed: {status} {body}")
