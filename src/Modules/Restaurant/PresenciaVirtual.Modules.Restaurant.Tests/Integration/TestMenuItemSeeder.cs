using Dapper;
using Npgsql;

namespace PresenciaVirtual.Modules.Restaurant.Tests.Integration;

/// <summary>Full Menu Management does not exist yet (out of scope of AddItem), so integration tests seed a menu item directly, mirroring TestTableSeeder.</summary>
public static class TestMenuItemSeeder
{
    public static async Task<Guid> SeedMenuItemAsync(string connectionString, Guid tenantId, decimal price = 5m, bool isAlcoholic = false)
    {
        var menuItemId = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await connection.ExecuteAsync("SELECT set_config('app.tenant_id', @tenantId, false);", new { tenantId = tenantId.ToString() });
        await connection.ExecuteAsync(
            "INSERT INTO restaurant.menu_items (id, tenant_id, name, price, is_alcoholic) VALUES (@menuItemId, @tenantId, @name, @price, @isAlcoholic);",
            new { menuItemId, tenantId, name = $"Item {menuItemId:N}", price, isAlcoholic });

        return menuItemId;
    }
}
