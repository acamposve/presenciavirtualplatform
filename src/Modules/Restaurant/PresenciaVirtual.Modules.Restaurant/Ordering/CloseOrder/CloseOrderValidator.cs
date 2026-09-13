namespace PresenciaVirtual.Modules.Restaurant.Ordering.CloseOrder;

public static class CloseOrderValidator
{
    /// <summary>Returns validation error messages; empty when the command is valid. OrderId itself is bound from a {orderId:guid} route segment, so malformed/missing values never reach here (they 404 at routing, per the Error Scenarios note in close-order.md).</summary>
    public static IReadOnlyList<string> Validate(CloseOrderCommand command)
    {
        var errors = new List<string>();

        if (command.IdempotencyKey is not null && command.IdempotencyKey.Length is 0 or > 200)
        {
            errors.Add("Idempotency-Key, when provided, must be between 1 and 200 characters.");
        }

        return errors;
    }
}
