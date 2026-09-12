using Dapper;
using PresenciaVirtual.Modules.Core.Persistence;
using PresenciaVirtual.Modules.Restaurant.Ordering;
using PresenciaVirtual.Modules.Restaurant.Ordering.AddItem;

namespace PresenciaVirtual.Modules.Restaurant.Infrastructure.Ordering;

/// <summary>
/// Implements AddItem's atomicity requirements (specs/restaurant/ordering/add-item.md, BR4/BR6/BR7)
/// using PostgreSQL advisory locks (pg_advisory_xact_lock) scoped to the transaction: one lock
/// per (tenant, order, menu item) line serializes the merge and the alcoholic-item limit check
/// together, and one lock per (tenant, idempotency key) — when a key is supplied — serializes
/// the idempotency claim. Locks are held for the duration of the transaction and released
/// automatically on commit or rollback, which is why a plain read-then-write (or relying on
/// unique-constraint-violation recovery, as CreateOrder does) is not used here: the limit check
/// specifically needs to see an up-to-date quantity before deciding, not just avoid crashing on
/// a conflicting write.
/// </summary>
public sealed class OrderItemRepository(ITenantDbConnectionFactory connectionFactory) : IOrderItemRepository
{
    public async Task<IReadOnlyList<OrderItem>> GetByOrderAsync(Guid tenantId, Guid orderId, CancellationToken cancellationToken = default)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = """
            SELECT id AS Id, order_id AS OrderId, menu_item_id AS MenuItemId, quantity AS Quantity, unit_price_snapshot AS UnitPriceSnapshot
            FROM restaurant.order_items
            WHERE tenant_id = @tenantId AND order_id = @orderId
            ORDER BY created_at;
            """;

        var rows = await connection.QueryAsync<OrderItem>(sql, new { tenantId, orderId });
        return rows.ToList();
    }

    public async Task<AddItemIdempotencyClaim?> FindIdempotencyClaimAsync(Guid tenantId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<AddItemIdempotencyClaim>(IdempotencyClaimSql, new { tenantId, idempotencyKey });
    }

    public async Task<AddItemOutcome> AddOrMergeAsync(AddItemMergeRequest request, CancellationToken cancellationToken = default)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        if (request.IdempotencyKey is { Length: > 0 } key)
        {
            await LockAsync(connection, transaction, $"additem-key:{request.TenantId}:{key}");
        }

        await LockAsync(connection, transaction, $"additem-line:{request.TenantId}:{request.OrderId}:{request.MenuItemId}");

        if (request.IdempotencyKey is { Length: > 0 } idempotencyKey)
        {
            // Re-checked here (under the lock above) as the race-safety net for the Handler's
            // own upfront FindIdempotencyClaimAsync call: a concurrent request may have claimed
            // this key in between.
            var existing = await connection.QuerySingleOrDefaultAsync<AddItemIdempotencyClaim>(
                IdempotencyClaimSql, new { request.TenantId, idempotencyKey }, transaction);

            if (existing is not null)
            {
                // Nothing to mutate either way; commit releases the advisory locks.
                transaction.Commit();

                var matches = existing.OrderId == request.OrderId
                    && existing.MenuItemId == request.MenuItemId
                    && existing.Quantity == request.Quantity;

                if (!matches)
                {
                    throw new AddItemIdempotencyKeyConflictException(idempotencyKey);
                }

                return AddItemOutcome.Replayed;
            }
        }

        var existingQuantity = await connection.ExecuteScalarAsync<int?>(
            """
            SELECT quantity FROM restaurant.order_items
            WHERE tenant_id = @TenantId AND order_id = @OrderId AND menu_item_id = @MenuItemId;
            """,
            request, transaction) ?? 0;

        // Checked before the alcohol-limit policy, and independent of it: this applies to every
        // menu item, not only ones with a configured limit. The column itself is a PostgreSQL
        // "integer" - without this guard, a line already near int.MaxValue would overflow at
        // the INSERT below (SQLSTATE 22003), escaping every catch clause the endpoint has as an
        // unhandled 500 instead of a proper client error.
        if (LineQuantityPolicy.WouldOverflow(existingQuantity, request.Quantity))
        {
            transaction.Rollback();
            throw new LineQuantityTooLargeException(request.MenuItemId);
        }

        if (AlcoholicItemLimitPolicy.Exceeds(existingQuantity, request.Quantity, request.MaxAlcoholicItemQuantityPerLine))
        {
            transaction.Rollback();
            throw new AlcoholicItemLimitExceededException(request.MenuItemId, request.MaxAlcoholicItemQuantityPerLine!.Value);
        }

        await connection.ExecuteAsync(
            """
            INSERT INTO restaurant.order_items (id, tenant_id, order_id, menu_item_id, quantity, unit_price_snapshot)
            VALUES (@Id, @TenantId, @OrderId, @MenuItemId, @Quantity, @UnitPriceSnapshot)
            ON CONFLICT (tenant_id, order_id, menu_item_id)
            DO UPDATE SET quantity = restaurant.order_items.quantity + excluded.quantity;
            """,
            new { Id = Guid.NewGuid(), request.TenantId, request.OrderId, request.MenuItemId, request.Quantity, request.UnitPriceSnapshot },
            transaction);

        if (request.IdempotencyKey is { Length: > 0 } keyToClaim)
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO restaurant.add_item_idempotency_keys (tenant_id, idempotency_key, order_id, menu_item_id, quantity)
                VALUES (@TenantId, @IdempotencyKey, @OrderId, @MenuItemId, @Quantity);
                """,
                new { request.TenantId, IdempotencyKey = keyToClaim, request.OrderId, request.MenuItemId, request.Quantity },
                transaction);
        }

        transaction.Commit();
        return AddItemOutcome.Applied;
    }

    private static Task LockAsync(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, string lockKey)
        => connection.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended(@lockKey, 0));", new { lockKey }, transaction);

    private const string IdempotencyClaimSql = """
        SELECT order_id AS OrderId, menu_item_id AS MenuItemId, quantity AS Quantity
        FROM restaurant.add_item_idempotency_keys
        WHERE tenant_id = @tenantId AND idempotency_key = @idempotencyKey;
        """;
}
