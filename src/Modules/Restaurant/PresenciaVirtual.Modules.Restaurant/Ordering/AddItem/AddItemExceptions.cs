namespace PresenciaVirtual.Modules.Restaurant.Ordering.AddItem;

/// <summary>AC2: the order does not exist, or belongs to a different tenant (treated identically to avoid revealing cross-tenant existence).</summary>
public sealed class OrderNotFoundException(Guid orderId) : Exception($"Order '{orderId}' was not found.");

/// <summary>AC3/BR1: the order exists but is not in the Open status.</summary>
public sealed class OrderNotOpenException(Guid orderId) : Exception($"Order '{orderId}' is not Open.");

/// <summary>AC4: the menu item does not exist, or belongs to a different tenant.</summary>
public sealed class MenuItemNotFoundException(Guid menuItemId) : Exception($"Menu item '{menuItemId}' was not found.");

/// <summary>AC14/BR7: adding this quantity would exceed the tenant's configured alcoholic-item limit for this line.</summary>
public sealed class AlcoholicItemLimitExceededException(Guid menuItemId, int limit) : Exception($"Adding this quantity to menu item '{menuItemId}' would exceed the configured limit of {limit} for this line.");

/// <summary>AC9/BR6: the idempotency key was already used for a different request.</summary>
public sealed class AddItemIdempotencyKeyConflictException(string idempotencyKey) : Exception($"Idempotency key '{idempotencyKey}' was already used for a different request.");

/// <summary>
/// The resulting line quantity would exceed what the database column can represent. Rejected
/// explicitly — independent of BR7's alcoholic-item limit, which does not apply to every menu
/// item — rather than allowed to reach the database and fail with an unhandled overflow error.
/// </summary>
public sealed class LineQuantityTooLargeException(Guid menuItemId) : Exception($"Adding this quantity to menu item '{menuItemId}' would exceed the maximum representable line quantity.");
