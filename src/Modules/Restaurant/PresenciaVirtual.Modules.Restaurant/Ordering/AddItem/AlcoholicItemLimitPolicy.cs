namespace PresenciaVirtual.Modules.Restaurant.Ordering.AddItem;

/// <summary>BR7: pure arithmetic, kept separate from the transactional/locking mechanics that call it.</summary>
public static class AlcoholicItemLimitPolicy
{
    public static bool Exceeds(int existingQuantity, int requestedQuantity, int? maxQuantityPerLine)
        // Widened to long: existingQuantity + requestedQuantity can each be near int.MaxValue,
        // and adding them as int could overflow (wrapping negative) right before the
        // comparison, silently returning false instead of correctly rejecting the request.
        => maxQuantityPerLine is { } limit && (long)existingQuantity + requestedQuantity > limit;
}
