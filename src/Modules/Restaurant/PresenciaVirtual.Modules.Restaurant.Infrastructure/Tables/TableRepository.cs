using Dapper;
using PresenciaVirtual.Modules.Core.Persistence;
using PresenciaVirtual.Modules.Restaurant.Paging;
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

    public async Task AddAsync(Table table, CancellationToken cancellationToken = default)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = """
            INSERT INTO restaurant.tables (id, tenant_id, label, created_at)
            VALUES (@Id, @TenantId, @Label, @CreatedAt);
            """;

        await connection.ExecuteAsync(sql, new { table.Id, table.TenantId, table.Label, table.CreatedAt });
    }

    public async Task<TablePage> GetPageAsync(Guid tenantId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        // Repeatable read, not the default read-committed: the page and the total count are two
        // statements, and the specification (FR3/BR5) requires them to agree with each other. Under
        // read-committed a concurrent CreateTable could commit between them and make totalCount
        // disagree with the page it describes.
        using var transaction = connection.BeginTransaction(System.Data.IsolationLevel.RepeatableRead);

        const string pageSql = """
            SELECT id, tenant_id, label, created_at
            FROM restaurant.tables
            WHERE tenant_id = @tenantId
            ORDER BY lower(label), id
            LIMIT @pageSize OFFSET @offset;
            """;

        var offset = PageRequest.Offset(page, pageSize);
        var rows = await connection.QueryAsync<TableRow>(pageSql, new { tenantId, pageSize, offset }, transaction);

        var totalCount = await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM restaurant.tables WHERE tenant_id = @tenantId;", new { tenantId }, transaction);

        transaction.Commit();

        return new TablePage([.. rows.Select(r => r.ToDomain())], totalCount);
    }

    // Npgsql returns "timestamptz" as DateTime (UTC), not DateTimeOffset; Dapper's constructor
    // matching requires an exact type match, so the mismatch must be converted explicitly.
    private sealed record TableRow(Guid Id, Guid Tenant_Id, string Label, DateTime Created_At)
    {
        public Table ToDomain() => Table.Reconstruct(Id, Tenant_Id, Label, new DateTimeOffset(DateTime.SpecifyKind(Created_At, DateTimeKind.Utc)));
    }
}
