namespace PresenciaVirtual.Modules.Restaurant.Ordering.AddItem;

/// <summary>BR7: pure arithmetic, kept separate from the transactional/locking mechanics that call it.</summary>
public static class AlcoholicItemLimitPolicy
{
    public static bool Exceeds(int existingQuantity, int requestedQuantity, int? maxQuantityPerLine)
        => maxQuantityPerLine is { } limit && existingQuantity + requestedQuantity > limit;
}
