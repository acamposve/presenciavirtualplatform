using Dapper;
using Npgsql;
using PresenciaVirtual.Modules.Core.Persistence;
using PresenciaVirtual.Modules.Restaurant.Ordering;
using PresenciaVirtual.Modules.Restaurant.Ordering.CloseOrder;
using PresenciaVirtual.Modules.Restaurant.Ordering.CreateOrder;

namespace PresenciaVirtual.Modules.Restaurant.Infrastructure.Ordering;

public sealed class OrderRepository(ITenantDbConnectionFactory connectionFactory, IIdempotencyStore idempotencyStore, IOrderItemRepository orderItemRepository) : IOrderRepository
{
    public async Task<bool> HasOpenOrderAsync(Guid tenantId, Guid tableId, CancellationToken cancellationToken = default)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = """
            SELECT EXISTS(
                SELECT 1 FROM restaurant.orders
                WHERE tenant_id = @tenantId AND table_id = @tableId AND status = 'Open'
            );
            """;

        return await connection.ExecuteScalarAsync<bool>(sql, new { tenantId, tableId });
    }

    private const string OpenOrderPerTableConstraint = "ux_restaurant_orders_open_per_table";
    private const string IdempotencyKeyPrimaryKeyConstraint = "order_idempotency_keys_pkey";

    public async Task AddAsync(Order order, string? idempotencyKey, CancellationToken cancellationToken = default)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        const string insertOrderSql = """
            INSERT INTO restaurant.orders (id, tenant_id, table_id, status, created_at, created_by_user_id)
            VALUES (@Id, @TenantId, @TableId, @Status, @CreatedAt, @CreatedByUserId);
            """;

        const string insertIdempotencySql = """
            INSERT INTO restaurant.order_idempotency_keys (tenant_id, idempotency_key, table_id, order_id)
            VALUES (@TenantId, @IdempotencyKey, @TableId, @OrderId);
            """;

        try
        {
            await connection.ExecuteAsync(insertOrderSql, new
            {
                order.Id,
                order.TenantId,
                order.TableId,
                Status = order.Status.ToString(),
                order.CreatedAt,
                order.CreatedByUserId,
            }, transaction);

            if (idempotencyKey is { Length: > 0 })
            {
                await connection.ExecuteAsync(insertIdempotencySql, new
                {
                    order.TenantId,
                    IdempotencyKey = idempotencyKey,
                    order.TableId,
                    OrderId = order.Id,
                }, transaction);
            }

            transaction.Commit();
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation && ex.ConstraintName == OpenOrderPerTableConstraint)
        {
            // The ux_restaurant_orders_open_per_table partial unique index is the authoritative
            // guarantee of BR2 under concurrent requests (see 0002_restaurant_orders.sql). It
            // fires before the idempotency insert below ever runs, so a same-table, same-key
            // race lands here too, not in the IdempotencyKeyPrimaryKeyConstraint branch -
            // without checking for that, BR6 would be violated (the loser would get a spurious
            // 409 instead of replaying the winner). Re-check for a matching record (now
            // committed, since the constraint only fires once the other transaction resolved)
            // before concluding this is a genuine, unrelated conflict.
            transaction.Rollback();

            if (idempotencyKey is { Length: > 0 })
            {
                var winner = await idempotencyStore.FindAsync(order.TenantId, idempotencyKey, cancellationToken);
                if (winner is not null)
                {
                    throw winner.TableId == order.TableId
                        ? new IdempotencyKeyRaceLostException(idempotencyKey)
                        : new IdempotencyKeyConflictException(idempotencyKey);
                }
            }

            throw new TableAlreadyHasOpenOrderException(order.TableId);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation && ex.ConstraintName == IdempotencyKeyPrimaryKeyConstraint)
        {
            // A concurrent request committed this idempotency key first. Roll back this order
            // entirely (it must not be left orphaned without its key) and let the caller replay
            // against the winning record instead (BR6).
            transaction.Rollback();
            throw new IdempotencyKeyRaceLostException(idempotencyKey!);
        }
    }

    public async Task<Order?> GetAsync(Guid tenantId, Guid orderId, CancellationToken cancellationToken = default)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = """
            SELECT id, tenant_id, table_id, status, created_at, created_by_user_id
            FROM restaurant.orders
            WHERE tenant_id = @tenantId AND id = @orderId;
            """;

        var row = await connection.QuerySingleOrDefaultAsync<OrderRow>(sql, new { tenantId, orderId });
        if (row is null)
        {
            return null;
        }

        // The aggregate must reflect its current items to report a correct Total (BR5) - a
        // caller that only needed the order's own fields (e.g. CreateOrder's status/table
        // checks) still gets a fully-formed, accurate Order, not one that silently lies about
        // Total being zero once AddItem has run.
        var items = await orderItemRepository.GetByOrderAsync(tenantId, orderId, cancellationToken);
        return row.ToDomain(items);
    }

    public async Task<Order?> GetOpenByTableAsync(Guid tenantId, Guid tableId, CancellationToken cancellationToken = default)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = """
            SELECT id, tenant_id, table_id, status, created_at, created_by_user_id
            FROM restaurant.orders
            WHERE tenant_id = @tenantId AND table_id = @tableId AND status = 'Open';
            """;

        var row = await connection.QuerySingleOrDefaultAsync<OrderRow>(sql, new { tenantId, tableId });
        if (row is null)
        {
            return null;
        }

        var items = await orderItemRepository.GetByOrderAsync(tenantId, row.Id, cancellationToken);
        return row.ToDomain(items);
    }

    public async Task<Order> CloseAsync(Guid tenantId, Guid orderId, string? idempotencyKey, CancellationToken cancellationToken = default)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        // BR7: the same order-scoped lock AddItem's merge acquires before checking/writing
        // Status - this is what actually serializes the two operations against each other.
        await LockAsync(connection, transaction, OrderLock.Key(tenantId, orderId));

        if (idempotencyKey is { Length: > 0 } key)
        {
            // BR3: the claim check runs before resolving whether OrderId exists at all, so a key
            // reused for a different order is deterministically a conflict regardless of what
            // that order turns out to be (nonexistent, cross-tenant, or a genuine different order
            // in this tenant) - see close-order.md's Error Scenarios note on this precedence.
            await LockAsync(connection, transaction, $"closeorder-key:{tenantId}:{key}");

            var claimedOrderId = await connection.QuerySingleOrDefaultAsync<Guid?>(
                "SELECT order_id FROM restaurant.close_order_idempotency_keys WHERE tenant_id = @tenantId AND idempotency_key = @key;",
                new { tenantId, key }, transaction);

            if (claimedOrderId is not null)
            {
                if (claimedOrderId != orderId)
                {
                    transaction.Rollback();
                    throw new CloseOrderIdempotencyKeyConflictException(key);
                }

                // A replay MUST NOT attempt to transition the order again (BR3); nothing was
                // mutated, so just release the locks and re-read the order's current state.
                transaction.Commit();
                return await GetAsync(tenantId, orderId, cancellationToken)
                    ?? throw new InvalidOperationException($"Order '{orderId}' was expected to still exist but was not found.");
            }
        }

        const string selectSql = """
            SELECT id, tenant_id, table_id, status, created_at, created_by_user_id
            FROM restaurant.orders
            WHERE tenant_id = @tenantId AND id = @orderId;
            """;
        var row = await connection.QuerySingleOrDefaultAsync<OrderRow>(selectSql, new { tenantId, orderId }, transaction);
        if (row is null)
        {
            transaction.Rollback();
            throw new CloseOrderNotFoundException(orderId);
        }

        var items = (await connection.QueryAsync<OrderItem>(ItemsSql, new { tenantId, orderId }, transaction)).ToList();
        var order = row.ToDomain(items);

        try
        {
            // BR1: the domain-level invariant, enforced independently of the database-level
            // guard below - both must agree for the transition to actually happen.
            order = order.Close();
        }
        catch (InvalidOperationException)
        {
            transaction.Rollback();
            throw new OrderAlreadyClosedException(orderId);
        }

        // BR4: belt-and-suspenders alongside the lock above - even if the lock were somehow
        // bypassed, this conditional UPDATE cannot silently corrupt an already-closed order.
        await connection.ExecuteAsync(
            "UPDATE restaurant.orders SET status = 'Closed' WHERE tenant_id = @tenantId AND id = @orderId AND status = 'Open';",
            new { tenantId, orderId }, transaction);

        if (idempotencyKey is { Length: > 0 } keyToClaim)
        {
            await connection.ExecuteAsync(
                "INSERT INTO restaurant.close_order_idempotency_keys (tenant_id, idempotency_key, order_id) VALUES (@tenantId, @keyToClaim, @orderId);",
                new { tenantId, keyToClaim, orderId }, transaction);
        }

        transaction.Commit();
        return order;
    }

    private static Task LockAsync(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, string lockKey)
        => connection.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended(@lockKey, 0));", new { lockKey }, transaction);

    private const string ItemsSql = """
        SELECT id AS Id, order_id AS OrderId, menu_item_id AS MenuItemId, quantity AS Quantity, unit_price_snapshot AS UnitPriceSnapshot
        FROM restaurant.order_items
        WHERE tenant_id = @tenantId AND order_id = @orderId
        ORDER BY created_at;
        """;

    // Npgsql returns "timestamptz" as DateTime (UTC), not DateTimeOffset; Dapper's constructor
    // matching requires an exact type match, so the mismatch must be converted explicitly.
    private sealed record OrderRow(Guid Id, Guid Tenant_Id, Guid Table_Id, string Status, DateTime Created_At, Guid Created_By_User_Id)
    {
        public Order ToDomain(IReadOnlyList<OrderItem> items) => Order.Reconstruct(Id, Tenant_Id, Table_Id, Created_By_User_Id, new DateTimeOffset(DateTime.SpecifyKind(Created_At, DateTimeKind.Utc)), Enum.Parse<OrderStatus>(Status), items);
    }
}
