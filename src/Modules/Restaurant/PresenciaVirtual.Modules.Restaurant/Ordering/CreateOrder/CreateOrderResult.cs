namespace PresenciaVirtual.Modules.Restaurant.Ordering.CreateOrder;

/// <summary>
/// <paramref name="IsReplay"/> distinguishes a brand-new order (AC1) from a replayed
/// idempotent request that returned a previously created order (AC7), so the endpoint can
/// return 201 Created versus 200 OK respectively.
/// </summary>
public sealed record CreateOrderResult(Guid OrderId, Guid TableId, OrderStatus Status, DateTimeOffset CreatedAt, bool IsReplay);
