# Larkspur Tea: shop backend

The order and checkout logic behind Larkspur Tea's online shop. Plain Python 3.13, no framework, no dependencies
beyond pytest for the tests.

- `shop/cart.py`: carts and items. Prices are in pence; weights are in kilograms, as the warehouse weighs them.
- `shop/orders.py`: orders and the order statuses the shop shows customers.
- `shop/shipping.py`: everything to do with our carrier, Tern. Mostly still to write.
- `shop/tern.py`: a thin client for Tern's API (authentication, JSON in and out).
- `shop/settings.py`: configuration, from the environment with defaults for development.

## Shipping

We ship everything with Tern. In development, `TERN_API_URL` points at Tern's sandbox and `TERN_API_KEY` holds a
test key; both default to what the sandbox expects.

## Tests

```
python -m pytest
```
