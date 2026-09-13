using PresenciaVirtual.Modules.Core.Security;
using PresenciaVirtual.Modules.Restaurant.Tables;

namespace PresenciaVirtual.Modules.Restaurant.Ordering.GetOrder;

public sealed class GetOrderHandler(
    ITableRepository tableRepository,
    IOrderRepository orderRepository,
    ICurrentUserContext currentUser)
{
    public async Task<GetOrderResult> HandleAsync(GetOrderQuery query, CancellationToken cancellationToken = default)
    {
        var tenantId = currentUser.TenantId;

        var tableExists = await tableRepository.ExistsForTenantAsync(tenantId, query.TableId, cancellationToken);
        if (!tableExists)
        {
            throw new GetOrderTableNotFoundException(query.TableId);
        }

        var order = await orderRepository.GetOpenByTableAsync(tenantId, query.TableId, cancellationToken)
            ?? throw new TableHasNoOpenOrderException(query.TableId);

        return GetOrderResult.From(order);
    }
}
