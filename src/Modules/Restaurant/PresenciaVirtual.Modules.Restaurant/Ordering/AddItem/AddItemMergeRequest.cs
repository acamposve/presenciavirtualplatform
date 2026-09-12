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

/// <summary>BR6: what a given Idempotency-Key previously applied, per tenant — used both for the Handler's upfront replay/conflict check and for the repository's own race-safety recheck.</summary>
public sealed record AddItemIdempotencyClaim(Guid OrderId, Guid MenuItemId, int Quantity);
