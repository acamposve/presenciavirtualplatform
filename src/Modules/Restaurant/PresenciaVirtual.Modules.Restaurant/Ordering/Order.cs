namespace PresenciaVirtual.Modules.Restaurant.Ordering;

/// <summary>
/// Aggregate root of the Ordering capability. Owns its line items (specs/restaurant/ordering/add-item.md)
/// and derives its Total from them (BR5); the ability to change status is introduced by future
/// specifications (CloseOrder, CancelOrder).
/// </summary>
public sealed class Order
{
    private Order(Guid id, Guid tenantId, Guid tableId, Guid createdByUserId, DateTimeOffset createdAt, IReadOnlyList<OrderItem> items)
    {
        Id = id;
        TenantId = tenantId;
        TableId = tableId;
        CreatedByUserId = createdByUserId;
        CreatedAt = createdAt;
        Status = OrderStatus.Open;
        Items = items;
    }

    public Guid Id { get; }

    public Guid TenantId { get; }

    public Guid TableId { get; }

    public Guid CreatedByUserId { get; }

    public DateTimeOffset CreatedAt { get; }

    public OrderStatus Status { get; }

    public IReadOnlyList<OrderItem> Items { get; }

    /// <summary>BR5: always the sum of the current items' line totals — never independently stored.</summary>
    public decimal Total => Items.Sum(i => i.LineTotal);

    /// <summary>Opens a new order for a table (BR4: an order always starts in the Open status, with no items).</summary>
    public static Order Open(Guid tenantId, Guid tableId, Guid createdByUserId, DateTimeOffset createdAt)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        if (tableId == Guid.Empty)
        {
            throw new ArgumentException("Table id is required.", nameof(tableId));
        }

        return new Order(Guid.NewGuid(), tenantId, tableId, createdByUserId, createdAt, items: []);
    }

    /// <summary>Rehydrates an existing order, including its current items, from persistence. Not for creating new orders — use <see cref="Open"/>.</summary>
    public static Order Reconstruct(Guid id, Guid tenantId, Guid tableId, Guid createdByUserId, DateTimeOffset createdAt, IReadOnlyList<OrderItem> items)
        => new(id, tenantId, tableId, createdByUserId, createdAt, items);
}
