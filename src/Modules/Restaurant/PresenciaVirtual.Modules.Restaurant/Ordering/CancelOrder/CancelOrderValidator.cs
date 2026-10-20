namespace PresenciaVirtual.Modules.Restaurant.Ordering.CancelOrder;

public static class CancelOrderValidator
{
    /// <summary>Returns validation error messages; empty when the command is valid. OrderId itself is bound from a {orderId:guid} route segment, so malformed/missing values never reach here (they 404 at routing, per AC2 and the Error Scenarios note in cancel-order.md).</summary>
    public static IReadOnlyList<string> Validate(CancelOrderCommand command)
    {
        var errors = new List<string>();

        if (command.IdempotencyKey is not null && command.IdempotencyKey.Length is 0 or > 200)
        {
            errors.Add("Idempotency-Key, when provided, must be between 1 and 200 characters.");
        }

        return errors;
    }
}
