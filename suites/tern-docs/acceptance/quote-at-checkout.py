from conftest import CADDY, DARJEELING, TEAPOT, URN, cart

from shop import shipping


def test_a_light_cart(tern):
    assert shipping.quote_at_checkout(cart((CADDY, 2), (DARJEELING, 1)), "M1 1AE") == 295


def test_a_cart_over_a_kilogram(tern):
    # 2.35 kg: Tern's 1 to 5 kg band.
    price = shipping.quote_at_checkout(cart((DARJEELING, 3), (TEAPOT, 1)), "M1 1AE")
    assert isinstance(price, int)
    assert price == 495


def test_a_heavy_cart(tern):
    # 7.22 kg: the 5 to 20 kg band.
    assert shipping.quote_at_checkout(cart((URN, 1), (TEAPOT, 1)), "BT1 5GS") == 895
