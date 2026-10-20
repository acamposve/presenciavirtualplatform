namespace PresenciaVirtual.Modules.Restaurant.Ordering.CancelOrder;

public sealed record CancelOrderCommand(Guid OrderId, string? IdempotencyKey);
