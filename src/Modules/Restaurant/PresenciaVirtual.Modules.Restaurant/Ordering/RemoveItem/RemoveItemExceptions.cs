namespace PresenciaVirtual.Modules.Restaurant.Ordering.RemoveItem;

/// <summary>
/// AC2: the order does not exist, or belongs to a different tenant (treated identically to avoid
/// revealing cross-tenant existence). Named distinctly from the other Ordering capabilities'
/// not-found exceptions since all of them are caught side-by-side in OrderEndpoints.cs.
/// </summary>
public sealed class RemoveItemOrderNotFoundException(Guid orderId) : Exception($"Order '{orderId}' was not found.");

/// <summary>
/// AC4/BR5: the order exists and is Open, but has no line for this MenuItemId — never added, or
/// already removed by an earlier request.
/// </summary>
public sealed class OrderItemNotFoundException(Guid orderId, Guid menuItemId) : Exception($"Order '{orderId}' has no line for menu item '{menuItemId}'.");

/// <summary>AC3/BR1: the order is not currently Open (already Closed or already Cancelled), so its contents are final.</summary>
public sealed class OrderNotRemovableException(Guid orderId, OrderStatus status) : Exception($"Order '{orderId}' cannot have items removed because its status is '{status}', not '{OrderStatus.Open}'.");

/// <summary>AC8/BR6: the idempotency key was already used for a different order or item.</summary>
public sealed class RemoveItemIdempotencyKeyConflictException(string idempotencyKey) : Exception($"Idempotency key '{idempotencyKey}' was already used to remove a line from a different order or menu item.");
