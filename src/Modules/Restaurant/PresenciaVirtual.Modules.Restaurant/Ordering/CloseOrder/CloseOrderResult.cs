namespace PresenciaVirtual.Modules.Restaurant.Ordering.CloseOrder;

public sealed record CloseOrderLine(Guid MenuItemId, int Quantity, decimal UnitPriceSnapshot, decimal LineTotal);

/// <summary>
/// FR7: the same full representation get-order.md returns (OrderId, TableId, Status, CreatedAt,
/// Items, Total) — this is effectively the order's final state at the moment of closing. Built
/// directly from the Order aggregate, not computed separately.
/// </summary>
public sealed record CloseOrderResult(Guid OrderId, Guid TableId, OrderStatus Status, DateTimeOffset CreatedAt, IReadOnlyList<CloseOrderLine> Items, decimal Total)
{
    public static CloseOrderResult From(Order order)
    {
        var lines = order.Items
            .Select(i => new CloseOrderLine(i.MenuItemId, i.Quantity, i.UnitPriceSnapshot, i.LineTotal))
            .ToList();

        return new CloseOrderResult(order.Id, order.TableId, order.Status, order.CreatedAt, lines, order.Total);
    }
}
