namespace PresenciaVirtual.Modules.Restaurant.Ordering.CreateOrder;

public sealed record IdempotencyRecord(Guid OrderId, Guid TableId);

/// <summary>
/// Backs BR6/AC7/AC8: reads which order a given idempotency key produced, per tenant. Writing
/// a record is not exposed here — it happens atomically with the order insert itself, via
/// <see cref="IOrderRepository.AddAsync"/>, so the two can never diverge.
/// </summary>
public interface IIdempotencyStore
{
    Task<IdempotencyRecord?> FindAsync(Guid tenantId, string idempotencyKey, CancellationToken cancellationToken = default);
}
