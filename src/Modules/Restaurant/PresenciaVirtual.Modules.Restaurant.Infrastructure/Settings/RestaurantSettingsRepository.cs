using Dapper;
using PresenciaVirtual.Modules.Core.Persistence;
using PresenciaVirtual.Modules.Restaurant.Settings;

namespace PresenciaVirtual.Modules.Restaurant.Infrastructure.Settings;

public sealed class RestaurantSettingsRepository(ITenantDbConnectionFactory connectionFactory) : IRestaurantSettingsRepository
{
    public async Task<int?> GetMaxAlcoholicItemQuantityPerLineAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = """
            SELECT max_alcoholic_item_quantity_per_line
            FROM restaurant.settings
            WHERE tenant_id = @tenantId;
            """;

        // A missing row means "no limit configured" (per BR7), not an error.
        return await connection.QuerySingleOrDefaultAsync<int?>(sql, new { tenantId });
    }
}
