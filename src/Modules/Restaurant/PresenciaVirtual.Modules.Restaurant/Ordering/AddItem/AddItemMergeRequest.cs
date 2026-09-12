namespace PresenciaVirtual.Modules.Restaurant.Ordering.AddItem;

public sealed record AddItemMergeRequest(
    Guid TenantId,
    Guid OrderId,
    Guid MenuItemId,
    int Quantity,
    decimal UnitPriceSnapshot,
    bool IsAlcoholic,
    int? MaxAlcoholicItemQuantityPerLine,
    string? IdempotencyKey);

public enum AddItemOutcome
{
    Applied,
    Replayed,
}
