namespace PresenciaVirtual.Modules.Restaurant.Ordering.RemoveItem;

public sealed record RemoveItemLine(Guid MenuItemId, int Quantity, decimal UnitPriceSnapshot, decimal LineTotal);

/// <summary>
/// FR7: the order's current items and total after the line is removed, in the same shape
/// `AddItem` returns (OrderId, Items, Total) — the order's own status and creation fields are
/// unaffected by removal, so they are deliberately absent.
/// </summary>
public sealed record RemoveItemResult(Guid OrderId, IReadOnlyList<RemoveItemLine> Items, decimal Total)
{
    public static RemoveItemResult From(Order order)
    {
        var lines = order.Items
            .Select(i => new RemoveItemLine(i.MenuItemId, i.Quantity, i.UnitPriceSnapshot, i.LineTotal))
            .ToList();

        return new RemoveItemResult(order.Id, lines, order.Total);
    }
}
