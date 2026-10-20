namespace PresenciaVirtual.Modules.Restaurant.Ordering.RemoveItem;

public static class RemoveItemValidator
{
    /// <summary>
    /// Returns validation error messages; empty when the command is valid. OrderId and MenuItemId
    /// are bound from {orderId:guid}/ {menuItemId:guid} route segments, so malformed or missing
    /// values never reach here (they 404 at routing, per AC2/AC5 and cancel-order.md's Error
    /// Scenarios note); MenuItemId is still checked for Guid.Empty as defence in depth, the same
    /// way AddItemValidator checks its own body-bound identifier.
    /// </summary>
    public static IReadOnlyList<string> Validate(RemoveItemCommand command)
    {
        var errors = new List<string>();

        if (command.MenuItemId == Guid.Empty)
        {
            errors.Add("menuItemId is required.");
        }

        if (command.IdempotencyKey is not null && command.IdempotencyKey.Length is 0 or > 200)
        {
            errors.Add("Idempotency-Key, when provided, must be between 1 and 200 characters.");
        }

        return errors;
    }
}
