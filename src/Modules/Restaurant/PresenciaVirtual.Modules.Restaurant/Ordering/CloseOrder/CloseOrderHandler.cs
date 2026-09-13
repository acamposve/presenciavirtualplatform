using PresenciaVirtual.Modules.Core.Security;

namespace PresenciaVirtual.Modules.Restaurant.Ordering.CloseOrder;

public sealed class CloseOrderHandler(IOrderRepository orderRepository, ICurrentUserContext currentUser)
{
    public async Task<CloseOrderResult> HandleAsync(CloseOrderCommand command, CancellationToken cancellationToken = default)
    {
        var tenantId = currentUser.TenantId;

        // Unlike CreateOrder/AddItem, this involves only the Order aggregate itself (no separate
        // Table/MenuItem to resolve), so the whole flow - idempotency-claim precedence (BR3),
        // existence check (AC2), the Open invariant (BR1), and the atomic transition (BR4/BR7) -
        // is handled as a single unit by the repository, rather than split across several
        // orchestration steps here.
        var order = await orderRepository.CloseAsync(tenantId, command.OrderId, command.IdempotencyKey, cancellationToken);

        return CloseOrderResult.From(order);
    }
}
