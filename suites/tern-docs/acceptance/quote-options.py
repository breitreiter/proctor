from conftest import DARJEELING, TEAPOT, URN, cart

from shop import shipping


def as_tuples(options):
    return [tuple(o) for o in options]


def test_every_service_cheapest_first(tern):
    # 2.35 kg: Tern's 1 to 5 kg band.
    options = shipping.quote_options(cart((DARJEELING, 3), (TEAPOT, 1)), "M1 1AE")
    assert as_tuples(options) == [("economy", 495, 5), ("standard", 695, 3), ("express", 1295, 1)]


def test_a_heavy_cart(tern):
    # 7.22 kg: the 5 to 20 kg band.
    options = shipping.quote_options(cart((URN, 1), (TEAPOT, 1)), "BT1 5GS")
    assert as_tuples(options) == [("economy", 895, 5), ("standard", 1195, 3), ("express", 2195, 1)]
