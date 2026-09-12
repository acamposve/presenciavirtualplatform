namespace PresenciaVirtual.Modules.Restaurant.Ordering.AddItem;

/// <summary>Guards the merge in BR4 against overflowing the "integer" quantity column — independent of BR7's alcoholic-item limit, which only some menu items have configured.</summary>
public static class LineQuantityPolicy
{
    public static bool WouldOverflow(int existingQuantity, int requestedQuantity)
        => (long)existingQuantity + requestedQuantity > int.MaxValue;
}
