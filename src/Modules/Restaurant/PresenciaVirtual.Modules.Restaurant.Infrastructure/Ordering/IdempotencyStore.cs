using Dapper;
using PresenciaVirtual.Modules.Core.Persistence;
using PresenciaVirtual.Modules.Restaurant.Ordering.CreateOrder;

namespace PresenciaVirtual.Modules.Restaurant.Infrastructure.Ordering;

public sealed class IdempotencyStore(ITenantDbConnectionFactory connectionFactory) : IIdempotencyStore
{
    public async Task<IdempotencyRecord?> FindAsync(Guid tenantId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = """
            SELECT order_id AS OrderId, table_id AS TableId
            FROM restaurant.order_idempotency_keys
            WHERE tenant_id = @tenantId AND idempotency_key = @idempotencyKey;
            """;

        return await connection.QuerySingleOrDefaultAsync<IdempotencyRecord>(sql, new { tenantId, idempotencyKey });
    }
}
