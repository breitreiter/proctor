from dataclasses import dataclass, field


@dataclass(frozen=True)
class Item:
    sku: str
    name: str
    price_pence: int
    weight_kg: float


@dataclass
class Cart:
    lines: list[tuple[Item, int]] = field(default_factory=list)

    def add(self, item: Item, quantity: int = 1) -> None:
        if quantity < 1:
            raise ValueError("quantity must be at least 1")
        self.lines.append((item, quantity))

    def subtotal_pence(self) -> int:
        return sum(item.price_pence * qty for item, qty in self.lines)

    def total_weight_kg(self) -> float:
        """The packed weight, as the warehouse scales read it."""
        return round(sum(item.weight_kg * qty for item, qty in self.lines), 3)
