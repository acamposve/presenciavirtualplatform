using Dapper;
using Npgsql;
using PresenciaVirtual.Modules.Core.Persistence;
using PresenciaVirtual.Modules.Restaurant.Ordering;
using PresenciaVirtual.Modules.Restaurant.Ordering.CreateOrder;

namespace PresenciaVirtual.Modules.Restaurant.Infrastructure.Ordering;

public sealed class OrderRepository(ITenantDbConnectionFactory connectionFactory) : IOrderRepository
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
            // guarantee of BR2 under concurrent requests (see 0002_restaurant_orders.sql).
            transaction.Rollback();
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
        return row?.ToDomain();
    }

    // Npgsql returns "timestamptz" as DateTime (UTC), not DateTimeOffset; Dapper's constructor
    // matching requires an exact type match, so the mismatch must be converted explicitly.
    private sealed record OrderRow(Guid Id, Guid Tenant_Id, Guid Table_Id, string Status, DateTime Created_At, Guid Created_By_User_Id)
    {
        public Order ToDomain() => Order.Reconstruct(Id, Tenant_Id, Table_Id, Created_By_User_Id, new DateTimeOffset(DateTime.SpecifyKind(Created_At, DateTimeKind.Utc)));
    }
}
