namespace PresenciaVirtual.Modules.Restaurant.Ordering.CancelOrder;

public sealed record CancelOrderLine(Guid MenuItemId, int Quantity, decimal UnitPriceSnapshot, decimal LineTotal);

/// <summary>
/// FR7: the same full representation get-order.md and close-order.md return (OrderId, TableId,
/// Status, CreatedAt, Items, Total) — effectively the order's final state at the moment of
/// cancelling. Built directly from the Order aggregate; none of these values is recomputed or
/// frozen separately (BR2/BR5: cancelling never alters the items or their derived total).
/// </summary>
public sealed record CancelOrderResult(Guid OrderId, Guid TableId, OrderStatus Status, DateTimeOffset CreatedAt, IReadOnlyList<CancelOrderLine> Items, decimal Total)
{
    public static CancelOrderResult From(Order order)
    {
        var lines = order.Items
            .Select(i => new CancelOrderLine(i.MenuItemId, i.Quantity, i.UnitPriceSnapshot, i.LineTotal))
            .ToList();

        return new CancelOrderResult(order.Id, order.TableId, order.Status, order.CreatedAt, lines, order.Total);
    }
}
