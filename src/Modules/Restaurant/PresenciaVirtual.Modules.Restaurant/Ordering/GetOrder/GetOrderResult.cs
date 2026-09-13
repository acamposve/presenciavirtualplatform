namespace PresenciaVirtual.Modules.Restaurant.Ordering.GetOrder;

public sealed record GetOrderLine(Guid MenuItemId, int Quantity, decimal UnitPriceSnapshot, decimal LineTotal);

/// <summary>
/// FR4: intentionally a superset of CreateOrder's and AddItem's response contracts, since
/// GetOrder is the one lookup capability that must stand on its own without prior context.
/// Built directly from the Order aggregate (the single source of truth for Items/Total,
/// per add-item.md BR5), not computed separately.
/// </summary>
public sealed record GetOrderResult(Guid OrderId, Guid TableId, OrderStatus Status, DateTimeOffset CreatedAt, IReadOnlyList<GetOrderLine> Items, decimal Total)
{
    public static GetOrderResult From(Order order)
    {
        var lines = order.Items
            .Select(i => new GetOrderLine(i.MenuItemId, i.Quantity, i.UnitPriceSnapshot, i.LineTotal))
            .ToList();

        return new GetOrderResult(order.Id, order.TableId, order.Status, order.CreatedAt, lines, order.Total);
    }
}
