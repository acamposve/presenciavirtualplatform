using Dapper;
using Npgsql;

namespace PresenciaVirtual.Modules.Restaurant.Tests.Integration;

/// <summary>There is no capability to configure RestaurantSettings yet (out of scope of AddItem, BR7), so integration tests seed it directly.</summary>
public static class TestRestaurantSettingsSeeder
{
    public static async Task SeedMaxAlcoholicItemQuantityPerLineAsync(string connectionString, Guid tenantId, int limit)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await connection.ExecuteAsync("SELECT set_config('app.tenant_id', @tenantId, false);", new { tenantId = tenantId.ToString() });
        await connection.ExecuteAsync(
            "INSERT INTO restaurant.settings (tenant_id, max_alcoholic_item_quantity_per_line) VALUES (@tenantId, @limit);",
            new { tenantId, limit });
    }
}
