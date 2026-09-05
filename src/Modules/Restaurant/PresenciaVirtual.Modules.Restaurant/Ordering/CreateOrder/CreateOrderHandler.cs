using PresenciaVirtual.Modules.Core.Security;
using PresenciaVirtual.Modules.Restaurant.Tables;

namespace PresenciaVirtual.Modules.Restaurant.Ordering.CreateOrder;

public sealed class CreateOrderHandler(
    ITableRepository tableRepository,
    IOrderRepository orderRepository,
    IIdempotencyStore idempotencyStore,
    ICurrentUserContext currentUser,
    TimeProvider timeProvider)
{
    public async Task<CreateOrderResult> HandleAsync(CreateOrderCommand command, CancellationToken cancellationToken = default)
    {
        var tenantId = currentUser.TenantId;

        if (command.IdempotencyKey is { Length: > 0 } idempotencyKey)
        {
            var existing = await idempotencyStore.FindAsync(tenantId, idempotencyKey, cancellationToken);
            if (existing is not null)
            {
                if (existing.TableId != command.TableId)
                {
                    throw new IdempotencyKeyConflictException(idempotencyKey);
                }

                var replayedOrder = await orderRepository.GetAsync(tenantId, existing.OrderId, cancellationToken)
                    ?? throw new InvalidOperationException($"Idempotency key '{idempotencyKey}' references order '{existing.OrderId}', which no longer exists.");

                return new CreateOrderResult(replayedOrder.Id, replayedOrder.TableId, replayedOrder.Status, replayedOrder.CreatedAt, IsReplay: true);
            }
        }

        var tableExists = await tableRepository.ExistsForTenantAsync(tenantId, command.TableId, cancellationToken);
        if (!tableExists)
        {
            throw new TableNotFoundException(command.TableId);
        }

        var hasOpenOrder = await orderRepository.HasOpenOrderAsync(tenantId, command.TableId, cancellationToken);
        if (hasOpenOrder)
        {
            throw new TableAlreadyHasOpenOrderException(command.TableId);
        }

        var order = Order.Open(tenantId, command.TableId, currentUser.UserId, timeProvider.GetUtcNow());

        // BR2 is ultimately guaranteed by a database constraint (see the restaurant.orders
        // migration): AddAsync throws TableAlreadyHasOpenOrderException on conflict, covering
        // the race between the check above and this write.
        await orderRepository.AddAsync(order, cancellationToken);

        if (command.IdempotencyKey is { Length: > 0 } keyToSave)
        {
            await idempotencyStore.SaveAsync(tenantId, keyToSave, order.TableId, order.Id, cancellationToken);
        }

        return new CreateOrderResult(order.Id, order.TableId, order.Status, order.CreatedAt, IsReplay: false);
    }
}
