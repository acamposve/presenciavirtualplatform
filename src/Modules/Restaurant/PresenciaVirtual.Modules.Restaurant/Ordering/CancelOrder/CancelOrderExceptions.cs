namespace PresenciaVirtual.Modules.Restaurant.Ordering.CancelOrder;

/// <summary>
/// AC2: the order does not exist, or belongs to a different tenant (treated identically to avoid
/// revealing cross-tenant existence). Named distinctly from AddItem's and CloseOrder's own
/// not-found exceptions since all three are caught side-by-side in OrderEndpoints.cs.
/// </summary>
public sealed class CancelOrderNotFoundException(Guid orderId) : Exception($"Order '{orderId}' was not found.");

/// <summary>
/// AC3/AC4/BR1: the order is not currently Open — already Cancelled (AC3) or already Closed
/// (AC4) — and no matching Idempotency-Key replay applies. Deliberately one exception for both
/// cases: cancel-order.md BR1 makes "is it Open" the single rule, and BR6 makes Closed and
/// Cancelled mutually exclusive terminal states, so neither is more special than the other.
/// </summary>
public sealed class OrderNotCancellableException(Guid orderId, OrderStatus status) : Exception($"Order '{orderId}' cannot be cancelled because its status is '{status}', not '{OrderStatus.Open}'.");

/// <summary>AC7/BR3: the idempotency key was already used to cancel a different order.</summary>
public sealed class CancelOrderIdempotencyKeyConflictException(string idempotencyKey) : Exception($"Idempotency key '{idempotencyKey}' was already used to cancel a different order.");
