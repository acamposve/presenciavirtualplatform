using Dapper;
using PresenciaVirtual.Modules.Core.Persistence;
using PresenciaVirtual.Modules.Restaurant.Menu;
using PresenciaVirtual.Modules.Restaurant.Paging;

namespace PresenciaVirtual.Modules.Restaurant.Infrastructure.Menu;

public sealed class MenuItemRepository(ITenantDbConnectionFactory connectionFactory) : IMenuItemRepository
{
    public async Task<MenuItem?> GetAsync(Guid tenantId, Guid menuItemId, CancellationToken cancellationToken = default)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = """
            SELECT id, tenant_id, name, price, is_alcoholic, created_at
            FROM restaurant.menu_items
            WHERE tenant_id = @tenantId AND id = @menuItemId;
            """;

        var row = await connection.QuerySingleOrDefaultAsync<MenuItemRow>(sql, new { tenantId, menuItemId });
        return row?.ToDomain();
    }

    public async Task AddAsync(MenuItem menuItem, CancellationToken cancellationToken = default)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = """
            INSERT INTO restaurant.menu_items (id, tenant_id, name, price, is_alcoholic, created_at)
            VALUES (@Id, @TenantId, @Name, @Price, @IsAlcoholic, @CreatedAt);
            """;

        await connection.ExecuteAsync(sql, new
        {
            menuItem.Id,
            menuItem.TenantId,
            menuItem.Name,
            menuItem.Price,
            menuItem.IsAlcoholic,
            menuItem.CreatedAt,
        });
    }

    public async Task<MenuItemPage> GetPageAsync(Guid tenantId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        // Repeatable read, not the default read-committed: the page and the total count are two
        // statements, and the specification (FR3/BR5) requires them to agree with each other. Under
        // read-committed a concurrent CreateMenuItem could commit between them and make
        // totalCount disagree with the page it describes.
        using var transaction = connection.BeginTransaction(System.Data.IsolationLevel.RepeatableRead);

        const string pageSql = """
            SELECT id, tenant_id, name, price, is_alcoholic, created_at
            FROM restaurant.menu_items
            WHERE tenant_id = @tenantId
            ORDER BY lower(name), id
            LIMIT @pageSize OFFSET @offset;
            """;

        var offset = PageRequest.Offset(page, pageSize);
        var rows = await connection.QueryAsync<MenuItemRow>(pageSql, new { tenantId, pageSize, offset }, transaction);

        var totalCount = await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM restaurant.menu_items WHERE tenant_id = @tenantId;", new { tenantId }, transaction);

        transaction.Commit();

        return new MenuItemPage([.. rows.Select(r => r.ToDomain())], totalCount);
    }

    // Npgsql returns "timestamptz" as DateTime (UTC), not DateTimeOffset; Dapper's constructor
    // matching requires an exact type match, so the mismatch must be converted explicitly.
    private sealed record MenuItemRow(Guid Id, Guid Tenant_Id, string Name, decimal Price, bool Is_Alcoholic, DateTime Created_At)
    {
        public MenuItem ToDomain() => MenuItem.Reconstruct(Id, Tenant_Id, Name, Price, Is_Alcoholic, new DateTimeOffset(DateTime.SpecifyKind(Created_At, DateTimeKind.Utc)));
    }
}
