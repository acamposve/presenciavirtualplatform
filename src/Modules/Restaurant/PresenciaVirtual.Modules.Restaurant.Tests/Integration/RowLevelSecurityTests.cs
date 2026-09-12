using Dapper;
using Npgsql;
using Xunit;

namespace PresenciaVirtual.Modules.Restaurant.Tests.Integration;

/// <summary>
/// Proves Row-Level Security itself blocks cross-tenant access — independent of the
/// application's own tenant_id filtering — by querying directly through the same
/// least-privilege role the application uses (see NpgsqlTenantDbConnectionFactory, ADR 0002).
/// A prior review correctly pointed out that RLS does not apply to a superuser (which is what
/// the admin/migration role is), so exercising it through that role would prove nothing.
/// </summary>
[Collection(ApiCollection.Name)]
public class RowLevelSecurityTests(ApiFixture fixture)
{
    [Fact]
    public async Task AppRole_CannotSeeAnotherTenantsTableEvenWithoutApplicationFiltering()
    {
        var ownerTenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, ownerTenantId);

        await using var connection = new NpgsqlConnection(fixture.AppRoleConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("SELECT set_config('app.tenant_id', @tenantId, false);", new { tenantId = otherTenantId.ToString() });

        // No "AND tenant_id = ..." here on purpose: this asks for the row by id alone, so a
        // result could only come back if RLS itself — not application code — were filtering.
        var visibleId = await connection.QuerySingleOrDefaultAsync<Guid?>(
            "SELECT id FROM restaurant.tables WHERE id = @tableId;", new { tableId });

        Assert.Null(visibleId);
    }

    [Fact]
    public async Task AppRole_SeesNoRowsAtAllWhenNoTenantContextIsSet()
    {
        var tenantId = Guid.NewGuid();
        await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);

        await using var connection = new NpgsqlConnection(fixture.AppRoleConnectionString);
        await connection.OpenAsync();
        // Pooled connections can carry a previous test's app.tenant_id (Npgsql does not reset
        // custom GUCs on returning a connection to the pool), so reset it explicitly rather
        // than assuming a fresh backend. RESET on a once-set custom GUC yields an empty string,
        // not NULL — the RLS policy's NULLIF(..., '') is what makes this fail closed (see
        // 0001_restaurant_tables.sql) instead of erroring on an invalid uuid cast.
        await connection.ExecuteAsync("RESET app.tenant_id;");

        var count = await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM restaurant.tables;");

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task AppRole_SeesItsOwnTenantsTable_WhenTenantContextIsSetCorrectly()
    {
        var tenantId = Guid.NewGuid();
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);

        await using var connection = new NpgsqlConnection(fixture.AppRoleConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("SELECT set_config('app.tenant_id', @tenantId, false);", new { tenantId = tenantId.ToString() });

        var visibleId = await connection.QuerySingleOrDefaultAsync<Guid?>(
            "SELECT id FROM restaurant.tables WHERE id = @tableId;", new { tableId });

        Assert.Equal(tableId, visibleId);
    }

    [Fact]
    public async Task AppRole_CannotInsertARowForAnotherTenant()
    {
        // specs/restaurant/tables/create-table.md's write-isolation requirement: CreateTable is
        // the first write capability exercised against these policies, so this proves the
        // WITH CHECK side of RLS (not just the read/USING side already covered above).
        var ownerTenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(fixture.AppRoleConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("SELECT set_config('app.tenant_id', @tenantId, false);", new { tenantId = otherTenantId.ToString() });

        var exception = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
            "INSERT INTO restaurant.tables (id, tenant_id, label) VALUES (@id, @tenantId, 'Should be rejected');",
            new { id = Guid.NewGuid(), tenantId = ownerTenantId }));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }
}
