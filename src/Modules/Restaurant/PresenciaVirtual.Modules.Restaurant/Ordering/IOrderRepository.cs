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
}
