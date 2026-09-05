namespace PresenciaVirtual.Modules.Restaurant.Ordering;

public interface IOrderRepository
{
    Task<bool> HasOpenOrderAsync(Guid tenantId, Guid tableId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a newly opened order. Implementations MUST guarantee BR2 (at most one Open
    /// order per table) even under concurrent calls — e.g. via a database uniqueness
    /// constraint — and throw <see cref="CreateOrder.TableAlreadyHasOpenOrderException"/> when
    /// that constraint is violated, rather than relying solely on <see cref="HasOpenOrderAsync"/>.
    /// </summary>
    Task AddAsync(Order order, CancellationToken cancellationToken = default);

    Task<Order?> GetAsync(Guid tenantId, Guid orderId, CancellationToken cancellationToken = default);
}
