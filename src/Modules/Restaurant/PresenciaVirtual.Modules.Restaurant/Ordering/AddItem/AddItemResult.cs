namespace PresenciaVirtual.Modules.Restaurant.Ordering.AddItem;

public sealed record AddItemLine(Guid MenuItemId, int Quantity, decimal UnitPriceSnapshot, decimal LineTotal);

/// <summary>
/// Always reflects the order's current state (FR7) — including for a replayed idempotent
/// request (BR6), which never returns a frozen snapshot of the original call. Built directly
/// from the Order aggregate, which is the single source of truth for Items/Total (BR5) — not
/// computed separately, so it can never drift from what a future direct load of the Order
/// would show.
/// </summary>
public sealed record AddItemResult(Guid OrderId, IReadOnlyList<AddItemLine> Items, decimal Total)
{
    public static AddItemResult From(Order order)
    {
        var lines = order.Items
            .Select(i => new AddItemLine(i.MenuItemId, i.Quantity, i.UnitPriceSnapshot, i.LineTotal))
            .ToList();

        return new AddItemResult(order.Id, lines, order.Total);
    }
}
