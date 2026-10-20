namespace PresenciaVirtual.Modules.Restaurant.Ordering;

/// <summary>
/// Aggregate root of the Ordering capability. Owns its line items (specs/restaurant/ordering/add-item.md),
/// derives its Total from them (BR5), and has two mutually exclusive terminal transitions from
/// Open: Open -> Closed (specs/restaurant/ordering/close-order.md) and Open -> Cancelled
/// (specs/restaurant/ordering/cancel-order.md).
/// </summary>
public sealed class Order
{
    private Order(Guid id, Guid tenantId, Guid tableId, Guid createdByUserId, DateTimeOffset createdAt, OrderStatus status, IReadOnlyList<OrderItem> items)
    {
        Id = id;
        TenantId = tenantId;
        TableId = tableId;
        CreatedByUserId = createdByUserId;
        CreatedAt = createdAt;
        Status = status;
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

        return new Order(Guid.NewGuid(), tenantId, tableId, createdByUserId, createdAt, OrderStatus.Open, items: []);
    }

    /// <summary>
    /// Rehydrates an existing order, including its current status and items, from persistence.
    /// Not for creating new orders — use <see cref="Open"/>. Must be given the order's actual
    /// persisted <paramref name="status"/> (specs/restaurant/ordering/close-order.md BR6) — a
    /// caller that always passes <see cref="OrderStatus.Open"/> regardless of what was actually
    /// persisted would silently misreport every closed order as still open.
    /// </summary>
    public static Order Reconstruct(Guid id, Guid tenantId, Guid tableId, Guid createdByUserId, DateTimeOffset createdAt, OrderStatus status, IReadOnlyList<OrderItem> items)
        => new(id, tenantId, tableId, createdByUserId, createdAt, status, items);

    /// <summary>
    /// Closes the order (specs/restaurant/ordering/close-order.md BR1): a pure status transition
    /// that leaves Items/Total untouched (BR5). This is the domain-level invariant, enforced
    /// independently of — and in addition to — the database-level guard (BR4) that protects the
    /// same rule under concurrency.
    /// </summary>
    /// <exception cref="InvalidOperationException">The order is not currently Open.</exception>
    public Order Close()
    {
        if (Status != OrderStatus.Open)
        {
            throw new InvalidOperationException($"Order '{Id}' cannot be closed because its status is '{Status}', not '{OrderStatus.Open}'.");
        }

        return new Order(Id, TenantId, TableId, CreatedByUserId, CreatedAt, OrderStatus.Closed, Items);
    }

    /// <summary>
    /// Voids the order (specs/restaurant/ordering/cancel-order.md BR1/BR5): the other terminal
    /// transition out of Open, structurally parallel to <see cref="Close"/>. Like closing, it is a
    /// pure status transition — an order with items stays cancellable and its Items/Total are left
    /// untouched (BR2/BR5), so voiding never rewrites history. This is the domain-level invariant,
    /// enforced independently of — and in addition to — the database-level guard (BR4) that
    /// protects the same rule under concurrency.
    /// </summary>
    /// <exception cref="InvalidOperationException">The order is not currently Open (e.g. already Closed or already Cancelled).</exception>
    public Order Cancel()
    {
        if (Status != OrderStatus.Open)
        {
            throw new InvalidOperationException($"Order '{Id}' cannot be cancelled because its status is '{Status}', not '{OrderStatus.Open}'.");
        }

        return new Order(Id, TenantId, TableId, CreatedByUserId, CreatedAt, OrderStatus.Cancelled, Items);
    }
}
