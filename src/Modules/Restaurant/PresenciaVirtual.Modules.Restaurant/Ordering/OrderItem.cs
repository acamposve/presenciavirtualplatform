namespace PresenciaVirtual.Modules.Restaurant.Ordering;

/// <summary>
/// A line on an Order (specs/restaurant/ordering/add-item.md). At most one per (OrderId,
/// MenuItemId) pair (BR4) — adding the same menu item again increases Quantity rather than
/// creating a second line. UnitPriceSnapshot is fixed when the line is first created (BR3) and
/// does not change when more quantity is merged into it.
/// </summary>
public sealed record OrderItem(Guid Id, Guid OrderId, Guid MenuItemId, int Quantity, decimal UnitPriceSnapshot)
{
    public decimal LineTotal => Quantity * UnitPriceSnapshot;
}
