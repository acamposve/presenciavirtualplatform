using Dapper;
using PresenciaVirtual.Modules.Core.Persistence;
using PresenciaVirtual.Modules.Restaurant.Menu;

namespace PresenciaVirtual.Modules.Restaurant.Infrastructure.Menu;

public sealed class MenuItemRepository(ITenantDbConnectionFactory connectionFactory) : IMenuItemRepository
{
    public async Task<MenuItem?> GetAsync(Guid tenantId, Guid menuItemId, CancellationToken cancellationToken = default)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = """
            SELECT id AS Id, tenant_id AS TenantId, name AS Name, price AS Price, is_alcoholic AS IsAlcoholic
            FROM restaurant.menu_items
            WHERE tenant_id = @tenantId AND id = @menuItemId;
            """;

        return await connection.QuerySingleOrDefaultAsync<MenuItem>(sql, new { tenantId, menuItemId });
    }
}
