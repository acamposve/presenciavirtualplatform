namespace PresenciaVirtual.Modules.Restaurant.Ordering.CreateOrder;

public sealed record CreateOrderCommand(Guid TableId, string? IdempotencyKey);
