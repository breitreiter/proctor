import pytest

from shop.cart import Cart, Item

DARJEELING = Item("T-DARJ-250", "Darjeeling, 250 g", 1250, 0.31)
TEAPOT = Item("P-POT-1L", "Cast iron teapot, 1 l", 4800, 1.42)


def test_subtotal_counts_quantities():
    cart = Cart()
    cart.add(DARJEELING, 2)
    cart.add(TEAPOT)
    assert cart.subtotal_pence() == 2 * 1250 + 4800


def test_weight_is_in_kilograms():
    cart = Cart()
    cart.add(DARJEELING, 3)
    cart.add(TEAPOT)
    assert cart.total_weight_kg() == pytest.approx(2.35)


def test_quantity_must_be_positive():
    with pytest.raises(ValueError):
        Cart().add(TEAPOT, 0)
