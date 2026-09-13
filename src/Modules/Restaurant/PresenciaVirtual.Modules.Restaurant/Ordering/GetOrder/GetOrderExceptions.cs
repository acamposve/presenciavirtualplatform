namespace PresenciaVirtual.Modules.Restaurant.Ordering.GetOrder;

/// <summary>
/// AC3/AC4: the table does not exist, or belongs to a different tenant (treated identically to
/// avoid revealing cross-tenant existence). Named distinctly from CreateOrder's own
/// TableNotFoundException (a different exception type, in a different namespace) because both
/// are caught side-by-side in OrderEndpoints.cs.
/// </summary>
public sealed class GetOrderTableNotFoundException(Guid tableId) : Exception($"Table '{tableId}' was not found.");

/// <summary>AC2: the table exists but has no order in the Open status.</summary>
public sealed class TableHasNoOpenOrderException(Guid tableId) : Exception($"Table '{tableId}' has no open order.");
