namespace PresenciaVirtual.Modules.Restaurant.Ordering.CreateOrder;

public sealed record IdempotencyRecord(Guid OrderId, Guid TableId);

/// <summary>Backs BR6/AC7/AC8: records which order a given idempotency key produced, per tenant.</summary>
public interface IIdempotencyStore
{
    Task<IdempotencyRecord?> FindAsync(Guid tenantId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task SaveAsync(Guid tenantId, string idempotencyKey, Guid tableId, Guid orderId, CancellationToken cancellationToken = default);
}
