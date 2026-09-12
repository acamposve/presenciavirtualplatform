namespace PresenciaVirtual.Modules.Restaurant.Ordering.CreateOrder;

public static class CreateOrderValidator
{
    /// <summary>Returns validation error messages; empty when the command is valid.</summary>
    public static IReadOnlyList<string> Validate(CreateOrderCommand command)
    {
        var errors = new List<string>();

        if (command.TableId == Guid.Empty)
        {
            errors.Add("tableId is required.");
        }

        if (command.IdempotencyKey is not null && command.IdempotencyKey.Length is 0 or > 200)
        {
            errors.Add("Idempotency-Key, when provided, must be between 1 and 200 characters.");
        }

        return errors;
    }
}
