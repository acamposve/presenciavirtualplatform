namespace PresenciaVirtual.Modules.Restaurant.Ordering.GetOrder;

public static class GetOrderValidator
{
    /// <summary>Returns validation error messages; empty when the query is valid. AC9: also catches a missing or malformed TableId, since the endpoint maps an absent/unparseable query-string value to Guid.Empty.</summary>
    public static IReadOnlyList<string> Validate(GetOrderQuery query)
    {
        var errors = new List<string>();

        if (query.TableId == Guid.Empty)
        {
            errors.Add("tableId is required.");
        }

        return errors;
    }
}
