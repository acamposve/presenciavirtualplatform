namespace PresenciaVirtual.Modules.Restaurant.Ordering;

public interface IOrderRepository
{
    Task<bool> HasOpenOrderAsync(Guid tenantId, Guid tableId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a newly opened order and, when <paramref name="idempotencyKey"/> is supplied,
    /// its idempotency record — atomically, in a single transaction, so a failure between the
    /// two can never leave an order without the key that is supposed to replay it (BR6).
    /// Implementations MUST guarantee BR2 (at most one Open order per table) even under
    /// concurrent calls — e.g. via a database uniqueness constraint — and throw
    /// <see cref="CreateOrder.TableAlreadyHasOpenOrderException"/> when that constraint is
    /// violated, rather than relying solely on <see cref="HasOpenOrderAsync"/>. Similarly, if a
    /// concurrent request commits the same idempotency key first, implementations MUST roll
    /// back this order and throw <see cref="CreateOrder.IdempotencyKeyRaceLostException"/> so
    /// the caller can replay against the winning record instead of leaving an orphaned order.
    /// </summary>
    Task AddAsync(Order order, string? idempotencyKey, CancellationToken cancellationToken = default);

    Task<Order?> GetAsync(Guid tenantId, Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>BR2: at most one Open order can exist per table, so this unambiguously identifies at most one order (specs/restaurant/ordering/get-order.md).</summary>
    Task<Order?> GetOpenByTableAsync(Guid tenantId, Guid tableId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes an Open order (specs/restaurant/ordering/close-order.md). Implementations MUST
    /// check an Idempotency-Key claim (if supplied) BEFORE resolving whether OrderId exists
    /// (BR3), throwing <see cref="CloseOrder.CloseOrderNotFoundException"/> if the order does
    /// not exist within the tenant, <see cref="CloseOrder.OrderAlreadyClosedException"/> if it
    /// is not Open (BR1/BR4) and no matching-key replay applies, and
    /// <see cref="CloseOrder.CloseOrderIdempotencyKeyConflictException"/> if the key was already
    /// used for a different order (BR3). The Open -> Closed transition MUST serialize against a
    /// concurrent AddItem call for the same order via the shared order-scoped lock (BR7), and the
    /// returned Order's items MUST be read within the same transaction as the transition, before
    /// it commits.
    /// </summary>
    Task<Order> CloseAsync(Guid tenantId, Guid orderId, string? idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels an Open order (specs/restaurant/ordering/cancel-order.md). Implementations MUST
    /// check an Idempotency-Key claim (if supplied) BEFORE resolving whether OrderId exists
    /// (BR3), throwing <see cref="CancelOrder.CancelOrderNotFoundException"/> if the order does
    /// not exist within the tenant, <see cref="CancelOrder.OrderNotCancellableException"/> if it
    /// is not Open — already Closed (AC4), already Cancelled (AC3), or lost a concurrent race
    /// (BR4) — and <see cref="CancelOrder.CancelOrderIdempotencyKeyConflictException"/> if the
    /// key was already used for a different order (BR3). The Open -> Cancelled transition MUST
    /// acquire the same order-scoped lock <see cref="CloseAsync"/> and AddItem use (BR7), MUST
    /// guard itself with a conditional update so only one concurrent transition can apply (BR4),
    /// and MUST NOT touch the order's items or total (BR2/BR5). The returned Order's items MUST be
    /// read within the same transaction as the transition, before it commits.
    /// </summary>
    Task<Order> CancelAsync(Guid tenantId, Guid orderId, string? idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes one line from an Open order (specs/restaurant/ordering/remove-item.md).
    /// Implementations MUST check an Idempotency-Key claim (if supplied) BEFORE resolving whether
    /// OrderId or MenuItemId exist (BR6), throwing
    /// <see cref="RemoveItem.RemoveItemOrderNotFoundException"/> if the order does not exist
    /// within the tenant (AC2), <see cref="RemoveItem.OrderNotRemovableException"/> if it is not
    /// Open (BR1/AC3), <see cref="RemoveItem.OrderItemNotFoundException"/> if there is no line for
    /// that menu item or it was already removed (AC4/BR5), and
    /// <see cref="RemoveItem.RemoveItemIdempotencyKeyConflictException"/> if the key was already
    /// used for a different order or item (BR6). The delete MUST be guarded by the affected-row
    /// count (BR5) and MUST serialize against every other mutation of the same order via the
    /// shared order-scoped lock (BR4), acquired in the same order those capabilities already use.
    /// The remaining items MUST be read within the same transaction as the delete, after it.
    /// </summary>
    Task<Order> RemoveItemAsync(Guid tenantId, Guid orderId, Guid menuItemId, string? idempotencyKey, CancellationToken cancellationToken = default);
}
