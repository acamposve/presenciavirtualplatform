namespace PresenciaVirtual.Modules.Restaurant.Ordering.CreateOrder;

/// <summary>AC3/AC6: the table does not exist, or belongs to a different tenant (treated identically to avoid revealing cross-tenant existence).</summary>
public sealed class TableNotFoundException(Guid tableId) : Exception($"Table '{tableId}' was not found.");

/// <summary>AC2/BR2: the table already has an order in the Open status.</summary>
public sealed class TableAlreadyHasOpenOrderException(Guid tableId) : Exception($"Table '{tableId}' already has an open order.");

/// <summary>AC8/BR6: the idempotency key was already used for a different request.</summary>
public sealed class IdempotencyKeyConflictException(string idempotencyKey) : Exception($"Idempotency key '{idempotencyKey}' was already used for a different request.");

/// <summary>
/// BR6: a concurrent request committed the same idempotency key first. The order just built
/// by this request was rolled back (never persisted); the caller must re-check
/// <see cref="IIdempotencyStore"/> for the winning record and replay/reject accordingly,
/// exactly as it would have if that record had existed from the start.
/// </summary>
public sealed class IdempotencyKeyRaceLostException(string idempotencyKey) : Exception($"Idempotency key '{idempotencyKey}' was committed by a concurrent request.");
