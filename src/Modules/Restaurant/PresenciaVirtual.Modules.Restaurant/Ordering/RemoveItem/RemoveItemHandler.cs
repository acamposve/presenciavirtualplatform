using PresenciaVirtual.Modules.Core.Security;

namespace PresenciaVirtual.Modules.Restaurant.Ordering.RemoveItem;

public sealed class RemoveItemHandler(IOrderRepository orderRepository, ICurrentUserContext currentUser)
{
    public async Task<RemoveItemResult> HandleAsync(RemoveItemCommand command, CancellationToken cancellationToken = default)
    {
        var tenantId = currentUser.TenantId;

        // Same shape as CloseOrderHandler/CancelOrderHandler, for the same reason: this touches
        // only the Order aggregate and its own lines (no separate Table or MenuItem entity to
        // resolve), so the whole flow — idempotency-claim precedence (BR6), order existence
        // (AC2), the Open invariant (BR1), the guarded delete (BR5), and the items read that
        // builds the response (FR7) — is handled as a single unit by the repository.
        var order = await orderRepository.RemoveItemAsync(tenantId, command.OrderId, command.MenuItemId, command.IdempotencyKey, cancellationToken);

        return RemoveItemResult.From(order);
    }
}
