namespace PresenciaVirtual.Modules.Restaurant.Ordering.AddItem;

public sealed record AddItemLine(Guid MenuItemId, int Quantity, decimal UnitPriceSnapshot, decimal LineTotal);

/// <summary>
/// Always reflects the order's current state (FR7) — including for a replayed idempotent
/// request (BR6), which never returns a frozen snapshot of the original call.
/// </summary>
public sealed record AddItemResult(Guid OrderId, IReadOnlyList<AddItemLine> Items, decimal Total)
{
    public static AddItemResult From(Guid orderId, IReadOnlyList<OrderItem> items)
    {
        var lines = items
            .Select(i => new AddItemLine(i.MenuItemId, i.Quantity, i.UnitPriceSnapshot, i.LineTotal))
            .ToList();

        return new AddItemResult(orderId, lines, lines.Sum(l => l.LineTotal));
    }
}
