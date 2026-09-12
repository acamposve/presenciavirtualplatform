namespace PresenciaVirtual.Modules.Restaurant.Ordering;

/// <summary>
/// Aggregate root of the Ordering capability. Only the fields and invariants required by the
/// CreateOrder specification (BR1, BR4, BR5) are modeled here; Items and the ability to change
/// status are introduced by future specifications (AddItem, CloseOrder, CancelOrder).
/// </summary>
public sealed class Order
{
    private Order(Guid id, Guid tenantId, Guid tableId, Guid createdByUserId, DateTimeOffset createdAt)
    {
        Id = id;
        TenantId = tenantId;
        TableId = tableId;
        CreatedByUserId = createdByUserId;
        CreatedAt = createdAt;
        Status = OrderStatus.Open;
    }

    public Guid Id { get; }

    public Guid TenantId { get; }

    public Guid TableId { get; }

    public Guid CreatedByUserId { get; }

    public DateTimeOffset CreatedAt { get; }

    public OrderStatus Status { get; }

    /// <summary>Always zero: items do not exist until the AddItem specification is implemented (BR5).</summary>
    public decimal Total => 0m;

    /// <summary>Opens a new order for a table (BR4: an order always starts in the Open status).</summary>
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

        return new Order(Guid.NewGuid(), tenantId, tableId, createdByUserId, createdAt);
    }

    /// <summary>Rehydrates an existing order from persistence. Not for creating new orders — use <see cref="Open"/>.</summary>
    public static Order Reconstruct(Guid id, Guid tenantId, Guid tableId, Guid createdByUserId, DateTimeOffset createdAt)
        => new(id, tenantId, tableId, createdByUserId, createdAt);
}
