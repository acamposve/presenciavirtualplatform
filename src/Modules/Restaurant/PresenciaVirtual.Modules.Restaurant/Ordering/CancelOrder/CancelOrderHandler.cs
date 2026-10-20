using PresenciaVirtual.Modules.Core.Security;

namespace PresenciaVirtual.Modules.Restaurant.Ordering.CancelOrder;

public sealed class CancelOrderHandler(IOrderRepository orderRepository, ICurrentUserContext currentUser)
{
    public async Task<CancelOrderResult> HandleAsync(CancelOrderCommand command, CancellationToken cancellationToken = default)
    {
        var tenantId = currentUser.TenantId;

        // Same shape as CloseOrderHandler, for the same reason: this capability involves only the
        // Order aggregate itself (no separate Table/MenuItem to resolve), so the whole flow —
        // idempotency-claim precedence (BR3), existence check (AC2), the Open invariant (BR1), and
        // the atomic transition (BR4) — is handled as a single unit by the repository.
        var order = await orderRepository.CancelAsync(tenantId, command.OrderId, command.IdempotencyKey, cancellationToken);

        return CancelOrderResult.From(order);
    }
}
