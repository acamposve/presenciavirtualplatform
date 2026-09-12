using PresenciaVirtual.Modules.Restaurant.Ordering.AddItem;

namespace PresenciaVirtual.Modules.Restaurant.Ordering;

public interface IOrderItemRepository
{
    Task<IReadOnlyList<OrderItem>> GetByOrderAsync(Guid tenantId, Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// BR6: looks up what a given Idempotency-Key already applied, without touching anything.
    /// Used by the Handler up front, so a reused key can be recognized as a replay or a
    /// conflict *before* validating that the request's own OrderId/MenuItemId resolve to real
    /// resources — a mismatched reuse against a nonexistent order must still be a 409, not a
    /// 404 (see AC9).
    /// </summary>
    Task<AddItemIdempotencyClaim?> FindIdempotencyClaimAsync(Guid tenantId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically creates or merges (BR4) a line for the given menu item, enforcing the
    /// alcoholic-item limit (BR7) and the idempotency claim (BR6) together with the update, as
    /// a single unit — see specs/restaurant/ordering/add-item.md's Data Requirements for why
    /// this cannot be split into separate read-then-write steps.
    /// </summary>
    Task<AddItemOutcome> AddOrMergeAsync(AddItemMergeRequest request, CancellationToken cancellationToken = default);
}
