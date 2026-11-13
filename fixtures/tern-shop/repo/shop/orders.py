from dataclasses import dataclass

from shop.cart import Cart

# The statuses a customer sees on their order page.
PAID = "paid"                # paid for, not yet booked with the carrier
PROCESSING = "processing"    # booked with the carrier, waiting to be collected
SHIPPED = "shipped"          # with the carrier and on its way
DELIVERED = "delivered"      # arrived
PROBLEM = "problem"          # something went wrong: a person needs to look at it
CANCELLED = "cancelled"      # cancelled before it left us

STATUSES = (PAID, PROCESSING, SHIPPED, DELIVERED, PROBLEM, CANCELLED)


@dataclass
class Order:
    id: str
    cart: Cart
    postcode: str
    status: str = PAID
    shipment_id: str | None = None
