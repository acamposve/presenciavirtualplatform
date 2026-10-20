using Dapper;
using Npgsql;
using PresenciaVirtual.Modules.Core.Persistence;
using PresenciaVirtual.Modules.Restaurant.Ordering;
using PresenciaVirtual.Modules.Restaurant.Ordering.CancelOrder;
using PresenciaVirtual.Modules.Restaurant.Ordering.CloseOrder;
using PresenciaVirtual.Modules.Restaurant.Ordering.CreateOrder;
using PresenciaVirtual.Modules.Restaurant.Ordering.RemoveItem;

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
                // mutated, so just release the locks and re-read the order's current state - on
                // this same, still-open connection rather than a fresh one via GetAsync, so a
                // replay does not hold two pooled connections at once (this one, uncommitted
                // until just below, plus a second one GetAsync would open) while waiting on it.
                var replayRow = await connection.QuerySingleAsync<OrderRow>(SelectOrderSql, new { tenantId, orderId }, transaction);
                var replayItems = (await connection.QueryAsync<OrderItem>(ItemsSql, new { tenantId, orderId }, transaction)).ToList();
                transaction.Commit();
                return replayRow.ToDomain(replayItems);
            }
        }

        var row = await connection.QuerySingleOrDefaultAsync<OrderRow>(SelectOrderSql, new { tenantId, orderId }, transaction);
        if (row is null)
        {
            transaction.Rollback();
            throw new CloseOrderNotFoundException(orderId);
        }

        try
        {
            // BR1: the domain-level invariant, enforced independently of the database-level
            // guard below - both must agree for the transition to actually happen. Items are
            // irrelevant to this check (Close() only inspects Status), so an empty placeholder
            // list is used here; the real items are (re-)read after the transition below, per
            // Data Requirements, and used to build the value this method actually returns.
            row.ToDomain([]).Close();
        }
        catch (InvalidOperationException)
        {
            transaction.Rollback();
            throw new OrderAlreadyClosedException(orderId);
        }

        // BR4: belt-and-suspenders alongside the lock above - the affected-row count is what
        // actually makes this a guard rather than a no-op: if the row were no longer Open (e.g.
        // the lock were somehow bypassed), this must not proceed to claim the key and commit as
        // if a transition had occurred.
        var rowsAffected = await connection.ExecuteAsync(
            "UPDATE restaurant.orders SET status = 'Closed' WHERE tenant_id = @tenantId AND id = @orderId AND status = 'Open';",
            new { tenantId, orderId }, transaction);

        if (rowsAffected != 1)
        {
            transaction.Rollback();
            throw new OrderAlreadyClosedException(orderId);
        }

        // Read after the UPDATE, still within this same open transaction (Data Requirements):
        // this is what lets a concurrent AddItem that legitimately committed just before this
        // UPDATE (AC13's "both succeed" case) actually show up in the response this method
        // returns, rather than a stale pre-transition read.
        var items = (await connection.QueryAsync<OrderItem>(ItemsSql, new { tenantId, orderId }, transaction)).ToList();
        var order = Order.Reconstruct(row.Id, row.Tenant_Id, row.Table_Id, row.Created_By_User_Id, new DateTimeOffset(DateTime.SpecifyKind(row.Created_At, DateTimeKind.Utc)), OrderStatus.Closed, items);

        if (idempotencyKey is { Length: > 0 } keyToClaim)
        {
            await connection.ExecuteAsync(
                "INSERT INTO restaurant.close_order_idempotency_keys (tenant_id, idempotency_key, order_id) VALUES (@tenantId, @keyToClaim, @orderId);",
                new { tenantId, keyToClaim, orderId }, transaction);
        }

        transaction.Commit();
        return order;
    }

    public async Task<Order> CancelAsync(Guid tenantId, Guid orderId, string? idempotencyKey, CancellationToken cancellationToken = default)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        // BR7: exactly the same order-scoped lock AddItem's merge and CloseOrder's transition
        // acquire - this is what serializes a cancel against a concurrent AddItem (AC15) and
        // against a concurrent CloseOrder (AC14), and it is acquired first for the same
        // single-lock-ordering reason OrderLock documents.
        await LockAsync(connection, transaction, OrderLock.Key(tenantId, orderId));

        if (idempotencyKey is { Length: > 0 } key)
        {
            // BR3: the claim check runs before resolving whether OrderId exists at all, so a key
            // reused for a different order is deterministically a conflict regardless of what
            // that order turns out to be (nonexistent, cross-tenant, or a genuine different order
            // in this tenant) - see cancel-order.md's Error Scenarios note on this precedence.
            await LockAsync(connection, transaction, $"cancelorder-key:{tenantId}:{key}");

            var claimedOrderId = await connection.QuerySingleOrDefaultAsync<Guid?>(
                "SELECT order_id FROM restaurant.cancel_order_idempotency_keys WHERE tenant_id = @tenantId AND idempotency_key = @key;",
                new { tenantId, key }, transaction);

            if (claimedOrderId is not null)
            {
                if (claimedOrderId != orderId)
                {
                    transaction.Rollback();
                    throw new CancelOrderIdempotencyKeyConflictException(key);
                }

                // A replay MUST NOT attempt to transition the order again (BR3), and MUST reflect
                // the order's CURRENT state rather than a frozen snapshot of the original
                // cancellation (AC8) - on this same, still-open connection rather than a fresh
                // one via GetAsync, so a replay does not hold two pooled connections at once.
                var replayRow = await connection.QuerySingleAsync<OrderRow>(SelectOrderSql, new { tenantId, orderId }, transaction);
                var replayItems = (await connection.QueryAsync<OrderItem>(ItemsSql, new { tenantId, orderId }, transaction)).ToList();
                transaction.Commit();
                return replayRow.ToDomain(replayItems);
            }
        }

        var row = await connection.QuerySingleOrDefaultAsync<OrderRow>(SelectOrderSql, new { tenantId, orderId }, transaction);
        if (row is null)
        {
            transaction.Rollback();
            throw new CancelOrderNotFoundException(orderId);
        }

        var persistedStatus = Enum.Parse<OrderStatus>(row.Status);

        try
        {
            // BR1: the domain-level invariant, enforced independently of the database-level guard
            // below - both must agree for the transition to actually happen. Items are irrelevant
            // to this check (Cancel() only inspects Status), so an empty placeholder list is used
            // here; the real items are (re-)read after the transition below, per Data Requirements.
            row.ToDomain([]).Cancel();
        }
        catch (InvalidOperationException)
        {
            transaction.Rollback();
            throw new OrderNotCancellableException(orderId, persistedStatus);
        }

        // BR4: the affected-row count is what actually makes this a guard rather than a no-op. It
        // is also what makes a race against a concurrent CloseOrder resolve to exactly one winner
        // (AC14): whichever transition commits first flips status away from 'Open', so the other's
        // UPDATE - still conditioned on status = 'Open' - affects zero rows and is rejected.
        var rowsAffected = await connection.ExecuteAsync(
            "UPDATE restaurant.orders SET status = 'Cancelled' WHERE tenant_id = @tenantId AND id = @orderId AND status = 'Open';",
            new { tenantId, orderId }, transaction);

        if (rowsAffected != 1)
        {
            transaction.Rollback();
            throw new OrderNotCancellableException(orderId, persistedStatus);
        }

        // Read after the UPDATE, still within this same open transaction (Data Requirements): a
        // concurrent AddItem that legitimately committed just before this UPDATE (AC15's "both
        // succeed" case) must still show up in the response, and nothing added after the
        // cancellation can be - AddItem's own guard rejects it once status is no longer Open.
        var items = (await connection.QueryAsync<OrderItem>(ItemsSql, new { tenantId, orderId }, transaction)).ToList();
        var order = Order.Reconstruct(row.Id, row.Tenant_Id, row.Table_Id, row.Created_By_User_Id, new DateTimeOffset(DateTime.SpecifyKind(row.Created_At, DateTimeKind.Utc)), OrderStatus.Cancelled, items);

        if (idempotencyKey is { Length: > 0 } keyToClaim)
        {
            await connection.ExecuteAsync(
                "INSERT INTO restaurant.cancel_order_idempotency_keys (tenant_id, idempotency_key, order_id) VALUES (@tenantId, @keyToClaim, @orderId);",
                new { tenantId, keyToClaim, orderId }, transaction);
        }

        transaction.Commit();
        return order;
    }

    public async Task<Order> RemoveItemAsync(Guid tenantId, Guid orderId, Guid menuItemId, string? idempotencyKey, CancellationToken cancellationToken = default)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        // BR4: the shared order-scoped lock, acquired first - the same lock and the same ordering
        // AddItem's merge, CloseOrder's transition and CancelOrder's transition already use, so a
        // removal serializes against all of them without any new lock and without deadlock.
        await LockAsync(connection, transaction, OrderLock.Key(tenantId, orderId));

        if (idempotencyKey is { Length: > 0 } key)
        {
            // BR6: the claim check runs before resolving whether the order or the line exists, so
            // a key reused for a different order or item is deterministically a conflict rather
            // than a 404 that depends on what those identifiers happen to resolve to.
            await LockAsync(connection, transaction, $"removeitem-key:{tenantId}:{key}");

            var claim = await connection.QuerySingleOrDefaultAsync<RemoveItemClaim>(
                "SELECT order_id AS OrderId, menu_item_id AS MenuItemId FROM restaurant.remove_item_idempotency_keys WHERE tenant_id = @tenantId AND idempotency_key = @key;",
                new { tenantId, key }, transaction);

            if (claim is not null)
            {
                if (claim.OrderId != orderId || claim.MenuItemId != menuItemId)
                {
                    transaction.Rollback();
                    throw new RemoveItemIdempotencyKeyConflictException(key);
                }

                // A replay MUST NOT delete again (BR6) and MUST reflect the order's current state
                // (AC9) - a second delete would report the line as not found even though the
                // original request succeeded. Re-read on this same, still-open connection rather
                // than via GetAsync, so a replay does not hold two pooled connections at once.
                var replayRow = await connection.QuerySingleAsync<OrderRow>(SelectOrderSql, new { tenantId, orderId }, transaction);
                var replayItems = (await connection.QueryAsync<OrderItem>(ItemsSql, new { tenantId, orderId }, transaction)).ToList();
                transaction.Commit();
                return replayRow.ToDomain(replayItems);
            }
        }

        var row = await connection.QuerySingleOrDefaultAsync<OrderRow>(SelectOrderSql, new { tenantId, orderId }, transaction);
        if (row is null)
        {
            transaction.Rollback();
            throw new RemoveItemOrderNotFoundException(orderId);
        }

        // BR1/AC3: a Closed or Cancelled order's contents are final. Checked before the line's own
        // existence, so an order that is both not Open and missing the line reports the status
        // conflict rather than a 404 that would imply the order is still open (remove-item.md's
        // Error Scenarios note).
        var persistedStatus = Enum.Parse<OrderStatus>(row.Status);
        if (persistedStatus != OrderStatus.Open)
        {
            transaction.Rollback();
            throw new OrderNotRemovableException(orderId, persistedStatus);
        }

        // BR5: the affected-row count is what makes this a guarded delete rather than a silent
        // no-op - it is also what makes two concurrent removals of the same line resolve to
        // exactly one success and one 404 (AC12), and what keeps a second removal from "succeeding"
        // after the first one already deleted the row.
        var rowsAffected = await connection.ExecuteAsync(
            "DELETE FROM restaurant.order_items WHERE tenant_id = @tenantId AND order_id = @orderId AND menu_item_id = @menuItemId;",
            new { tenantId, orderId, menuItemId }, transaction);

        if (rowsAffected != 1)
        {
            transaction.Rollback();
            throw new OrderItemNotFoundException(orderId, menuItemId);
        }

        // Read after the DELETE, in the same transaction (Data Requirements): the response reflects
        // exactly the lines that survived, never a stale pre-delete list.
        var items = (await connection.QueryAsync<OrderItem>(ItemsSql, new { tenantId, orderId }, transaction)).ToList();

        if (idempotencyKey is { Length: > 0 } keyToClaim)
        {
            await connection.ExecuteAsync(
                "INSERT INTO restaurant.remove_item_idempotency_keys (tenant_id, idempotency_key, order_id, menu_item_id) VALUES (@tenantId, @keyToClaim, @orderId, @menuItemId);",
                new { tenantId, keyToClaim, orderId, menuItemId }, transaction);
        }

        transaction.Commit();
        return row.ToDomain(items);
    }

    private static Task LockAsync(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, string lockKey)
        => connection.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended(@lockKey, 0));", new { lockKey }, transaction);
    private const string ItemsSql = """
        SELECT id AS Id, order_id AS OrderId, menu_item_id AS MenuItemId, quantity AS Quantity, unit_price_snapshot AS UnitPriceSnapshot
        FROM restaurant.order_items
        WHERE tenant_id = @tenantId AND order_id = @orderId
        ORDER BY created_at;
        """;

    private const string SelectOrderSql = """
        SELECT id, tenant_id, table_id, status, created_at, created_by_user_id
        FROM restaurant.orders
        WHERE tenant_id = @tenantId AND id = @orderId;
        """;

    // Npgsql returns "timestamptz" as DateTime (UTC), not DateTimeOffset; Dapper's constructor
    // matching requires an exact type match, so the mismatch must be converted explicitly.
    private sealed record OrderRow(Guid Id, Guid Tenant_Id, Guid Table_Id, string Status, DateTime Created_At, Guid Created_By_User_Id)
    {
        public Order ToDomain(IReadOnlyList<OrderItem> items) => Order.Reconstruct(Id, Tenant_Id, Table_Id, Created_By_User_Id, new DateTimeOffset(DateTime.SpecifyKind(Created_At, DateTimeKind.Utc)), Enum.Parse<OrderStatus>(Status), items);
    }

    /// <summary>BR6: the identifiers a remove-item idempotency key was originally claimed for.</summary>
    private sealed record RemoveItemClaim(Guid OrderId, Guid MenuItemId);
}
