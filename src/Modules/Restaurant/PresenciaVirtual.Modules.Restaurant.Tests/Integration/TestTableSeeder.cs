using Dapper;
using Npgsql;

namespace PresenciaVirtual.Modules.Restaurant.Tests.Integration;

/// <summary>
/// Table Management does not exist yet (out of scope of CreateOrder), so integration tests
/// seed a table row directly. Row-Level Security is FORCE-enabled (ADR 0002), so the tenant
/// session variable must be set before the insert, exactly as the application does.
/// </summary>
public static class TestTableSeeder
{
    public static async Task<Guid> SeedTableAsync(string connectionString, Guid tenantId)
    {
        var tableId = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await connection.ExecuteAsync("SELECT set_config('app.tenant_id', @tenantId, false);", new { tenantId = tenantId.ToString() });
        await connection.ExecuteAsync(
            "INSERT INTO restaurant.tables (id, tenant_id, label) VALUES (@tableId, @tenantId, @label);",
            new { tableId, tenantId, label = $"Table {tableId:N}" });

        return tableId;
    }
}
