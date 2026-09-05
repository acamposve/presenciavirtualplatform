using Dapper;
using PresenciaVirtual.Modules.Core.Persistence;
using PresenciaVirtual.Modules.Restaurant.Tables;

namespace PresenciaVirtual.Modules.Restaurant.Infrastructure.Tables;

public sealed class TableRepository(ITenantDbConnectionFactory connectionFactory) : ITableRepository
{
    public async Task<bool> ExistsForTenantAsync(Guid tenantId, Guid tableId, CancellationToken cancellationToken = default)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        // The tenant_id filter is explicit application-level enforcement (ADR 0002 rule 4);
        // Row-Level Security on restaurant.tables is the defense-in-depth layer underneath it.
        const string sql = "SELECT EXISTS(SELECT 1 FROM restaurant.tables WHERE id = @tableId AND tenant_id = @tenantId);";

        return await connection.ExecuteScalarAsync<bool>(sql, new { tableId, tenantId });
    }
}
