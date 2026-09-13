namespace PresenciaVirtual.Modules.Restaurant.Ordering.CloseOrder;

/// <summary>
/// AC2: the order does not exist, or belongs to a different tenant (treated identically to avoid
/// revealing cross-tenant existence). Named distinctly from AddItem's own OrderNotFoundException
/// since both are caught side-by-side in OrderEndpoints.cs.
/// </summary>
public sealed class CloseOrderNotFoundException(Guid orderId) : Exception($"Order '{orderId}' was not found.");

/// <summary>AC3/BR1: the order is not currently Open (already Closed), and no matching Idempotency-Key replay applies.</summary>
public sealed class OrderAlreadyClosedException(Guid orderId) : Exception($"Order '{orderId}' is already closed.");

/// <summary>AC7/BR3: the idempotency key was already used to close a different order.</summary>
public sealed class CloseOrderIdempotencyKeyConflictException(string idempotencyKey) : Exception($"Idempotency key '{idempotencyKey}' was already used to close a different order.");
