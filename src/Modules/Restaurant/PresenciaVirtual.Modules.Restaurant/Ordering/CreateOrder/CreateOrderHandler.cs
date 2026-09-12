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
                return await ReplayAsync(tenantId, idempotencyKey, existing, command.TableId, cancellationToken);
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

        try
        {
            // BR2 (open-order-per-table) and BR6 (idempotency key uniqueness) are ultimately
            // guaranteed by database constraints, applied atomically with the order insert
            // itself (see the restaurant.orders / restaurant.order_idempotency_keys
            // migrations) — this covers races against the checks above.
            await orderRepository.AddAsync(order, command.IdempotencyKey, cancellationToken);
        }
        catch (IdempotencyKeyRaceLostException) when (command.IdempotencyKey is { Length: > 0 } racedKey)
        {
            var winner = await idempotencyStore.FindAsync(tenantId, racedKey, cancellationToken)
                ?? throw new InvalidOperationException($"Idempotency key '{racedKey}' was reported as raced but no record was found afterward.");

            return await ReplayAsync(tenantId, racedKey, winner, command.TableId, cancellationToken);
        }

        return new CreateOrderResult(order.Id, order.TableId, order.Status, order.CreatedAt, IsReplay: false);
    }

    private async Task<CreateOrderResult> ReplayAsync(Guid tenantId, string idempotencyKey, IdempotencyRecord existing, Guid requestedTableId, CancellationToken cancellationToken)
    {
        if (existing.TableId != requestedTableId)
        {
            throw new IdempotencyKeyConflictException(idempotencyKey);
        }

        var replayedOrder = await orderRepository.GetAsync(tenantId, existing.OrderId, cancellationToken)
            ?? throw new InvalidOperationException($"Idempotency key '{idempotencyKey}' references order '{existing.OrderId}', which no longer exists.");

        return new CreateOrderResult(replayedOrder.Id, replayedOrder.TableId, replayedOrder.Status, replayedOrder.CreatedAt, IsReplay: true);
    }
}
