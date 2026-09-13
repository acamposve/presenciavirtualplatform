namespace PresenciaVirtual.Modules.Restaurant.Ordering.CloseOrder;

public sealed record CloseOrderCommand(Guid OrderId, string? IdempotencyKey);
