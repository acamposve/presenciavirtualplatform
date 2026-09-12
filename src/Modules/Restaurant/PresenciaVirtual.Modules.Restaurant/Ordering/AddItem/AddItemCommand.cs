namespace PresenciaVirtual.Modules.Restaurant.Ordering.AddItem;

public sealed record AddItemCommand(Guid OrderId, Guid MenuItemId, int Quantity, string? IdempotencyKey);
