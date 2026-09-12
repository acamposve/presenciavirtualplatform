namespace PresenciaVirtual.Modules.Restaurant.Ordering.AddItem;

public static class AddItemValidator
{
    /// <summary>Returns validation error messages; empty when the command is valid.</summary>
    public static IReadOnlyList<string> Validate(AddItemCommand command)
    {
        var errors = new List<string>();

        if (command.MenuItemId == Guid.Empty)
        {
            errors.Add("menuItemId is required.");
        }

        if (command.Quantity <= 0)
        {
            errors.Add("quantity must be a positive integer.");
        }

        if (command.IdempotencyKey is not null && command.IdempotencyKey.Length is 0 or > 200)
        {
            errors.Add("Idempotency-Key, when provided, must be between 1 and 200 characters.");
        }

        return errors;
    }
}
