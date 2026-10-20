namespace PresenciaVirtual.Modules.Restaurant.Ordering.RemoveItem;

public sealed record RemoveItemCommand(Guid OrderId, Guid MenuItemId, string? IdempotencyKey);
