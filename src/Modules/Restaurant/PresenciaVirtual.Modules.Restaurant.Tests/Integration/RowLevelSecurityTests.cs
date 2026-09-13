using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
    public async Task AppRole_ReadsOnlyItsOwnTenantsSettings()
    {
        // specs/restaurant/settings/update-restaurant-settings.md's read-isolation requirement:
        // restaurant.settings has no non-tenant key, so the only genuine way to prove RLS (not
        // application code) is filtering is a fully unfiltered query - any "WHERE tenant_id = "
        // clause would already produce the same result on its own, proving nothing about RLS.
        var ownerTenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(fixture.AppRoleConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("SELECT set_config('app.tenant_id', @tenantId, false);", new { tenantId = ownerTenantId.ToString() });
        await connection.ExecuteAsync(
            "INSERT INTO restaurant.settings (tenant_id, max_alcoholic_item_quantity_per_line) VALUES (@ownerTenantId, 5);",
            new { ownerTenantId });

        var ownCount = await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM restaurant.settings;");
        Assert.Equal(1, ownCount);

        await connection.ExecuteAsync("SELECT set_config('app.tenant_id', @tenantId, false);", new { tenantId = otherTenantId.ToString() });
        var otherCount = await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM restaurant.settings;");
        Assert.Equal(0, otherCount);
    }

    [Fact]
    public async Task AppRole_CannotUpsertAnotherTenantsSettings()
    {
        // specs/restaurant/settings/update-restaurant-settings.md's write-isolation requirement:
        // this is restaurant.settings's first write-isolation test. A successful same-tenant
        // upsert must succeed first, proving the new grants migration actually granted write
        // access - otherwise this test could not tell "blocked by RLS" apart from "blocked
        // because the grants migration was never applied" (both fail identically).
        var ownerTenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(fixture.AppRoleConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("SELECT set_config('app.tenant_id', @tenantId, false);", new { tenantId = ownerTenantId.ToString() });

        await connection.ExecuteAsync(
            "INSERT INTO restaurant.settings (tenant_id, max_alcoholic_item_quantity_per_line) VALUES (@ownerTenantId, 5);",
            new { ownerTenantId });

        await connection.ExecuteAsync("SELECT set_config('app.tenant_id', @tenantId, false);", new { tenantId = otherTenantId.ToString() });

        var exception = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
            "INSERT INTO restaurant.settings (tenant_id, max_alcoholic_item_quantity_per_line) VALUES (@ownerTenantId, 10) ON CONFLICT (tenant_id) DO UPDATE SET max_alcoholic_item_quantity_per_line = excluded.max_alcoholic_item_quantity_per_line;",
            new { ownerTenantId }));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    [Fact]
    public async Task AppRole_CannotInsertOrSeeAnotherTenantsMenuItem()
    {
        // specs/restaurant/menu/create-menu-item.md's write-isolation requirement:
        // restaurant.menu_items has had RLS enabled since add-item.md, but only ever been
        // exercised as a read (by AddItem's own lookup); this is its first write-isolation test.
        var ownerTenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(fixture.AppRoleConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("SELECT set_config('app.tenant_id', @tenantId, false);", new { tenantId = ownerTenantId.ToString() });

        // A successful same-tenant insert first: without this, a test that only exercises the
        // cross-tenant attempt below cannot tell "blocked by RLS" apart from "blocked because the
        // 0014_menu_item_grants.sql migration was never applied" - both fail identically with
        // InsufficientPrivilege.
        var ownMenuItemId = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO restaurant.menu_items (id, tenant_id, name, price, is_alcoholic) VALUES (@ownMenuItemId, @ownerTenantId, 'Coke', 3.00, false);",
            new { ownMenuItemId, ownerTenantId });

        await connection.ExecuteAsync("SELECT set_config('app.tenant_id', @tenantId, false);", new { tenantId = otherTenantId.ToString() });

        // No "AND tenant_id = ..." on purpose: a result could only come back if RLS itself were
        // filtering, not application code.
        var visibleId = await connection.QuerySingleOrDefaultAsync<Guid?>(
            "SELECT id FROM restaurant.menu_items WHERE id = @ownMenuItemId;", new { ownMenuItemId });
        Assert.Null(visibleId);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
            "INSERT INTO restaurant.menu_items (id, tenant_id, name, price, is_alcoholic) VALUES (@id, @ownerTenantId, 'Should be rejected', 1.00, false);",
            new { id = Guid.NewGuid(), ownerTenantId }));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
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

    [Fact]
    public async Task AppRole_CannotSeeAnotherTenantsOrderOrItsItemsEvenWithoutApplicationFiltering()
    {
        // specs/restaurant/ordering/get-order.md's cross-tenant isolation requirement: unlike
        // the table-read test above, this exercises restaurant.orders and restaurant.order_items
        // directly (GetOrder's own reads), not only restaurant.tables.
        var ownerTenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();

        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwtTokenFactory.CreateToken(ownerTenantId, Guid.NewGuid(), "restaurant.orders.create", "restaurant.orders.additem"));
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, ownerTenantId);
        var orderResponse = await client.PostAsJsonAsync("/api/v1/restaurants/orders", new { tableId });
        var orderBody = await orderResponse.Content.ReadFromJsonAsync<JsonElement>();
        var orderId = orderBody.GetProperty("orderId").GetGuid();
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, ownerTenantId);
        var addItemResponse = await client.PostAsJsonAsync($"/api/v1/restaurants/orders/{orderId}/items", new { menuItemId, quantity = 1 });
        // Without this, a broken AddItem call would silently leave order_items empty and this
        // test would still pass (0 visible rows either way) without ever exercising its isolation.
        Assert.Equal(HttpStatusCode.OK, addItemResponse.StatusCode);

        await using var connection = new NpgsqlConnection(fixture.AppRoleConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("SELECT set_config('app.tenant_id', @tenantId, false);", new { tenantId = otherTenantId.ToString() });

        // No "AND tenant_id = ..." on either query, on purpose: a result could only come back if
        // RLS itself — not application code — were filtering.
        var visibleOrderId = await connection.QuerySingleOrDefaultAsync<Guid?>(
            "SELECT id FROM restaurant.orders WHERE id = @orderId;", new { orderId });
        var visibleItemCount = await connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM restaurant.order_items WHERE order_id = @orderId;", new { orderId });

        Assert.Null(visibleOrderId);
        Assert.Equal(0, visibleItemCount);
    }

    [Fact]
    public async Task AppRole_CannotSeeOrInsertAnotherTenantsCloseOrderIdempotencyKey()
    {
        // specs/restaurant/ordering/close-order.md's cross-tenant isolation requirement for its
        // own new table (close_order_idempotency_keys) - both the read and the WITH CHECK insert
        // side, following the same pattern as the orders/order_items and tables tests above.
        var ownerTenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();

        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwtTokenFactory.CreateToken(ownerTenantId, Guid.NewGuid(), "restaurant.orders.create", "restaurant.orders.close"));
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, ownerTenantId);
        var orderResponse = await client.PostAsJsonAsync("/api/v1/restaurants/orders", new { tableId });
        var orderId = (await orderResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderId").GetGuid();
        var closeResponse = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/v1/restaurants/orders/{orderId}/close")
        {
            Headers = { { "Idempotency-Key", "rls-test-key" } },
        });
        Assert.Equal(HttpStatusCode.OK, closeResponse.StatusCode);

        await using var connection = new NpgsqlConnection(fixture.AppRoleConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("SELECT set_config('app.tenant_id', @tenantId, false);", new { tenantId = otherTenantId.ToString() });

        // No "AND tenant_id = ..." on purpose: a result could only come back if RLS itself were
        // filtering, not application code.
        var visibleOrderId = await connection.QuerySingleOrDefaultAsync<Guid?>(
            "SELECT order_id FROM restaurant.close_order_idempotency_keys WHERE idempotency_key = 'rls-test-key';");
        Assert.Null(visibleOrderId);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
            "INSERT INTO restaurant.close_order_idempotency_keys (tenant_id, idempotency_key, order_id) VALUES (@ownerTenantId, 'rls-test-key-2', @orderId);",
            new { ownerTenantId, orderId }));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    [Fact]
    public async Task AppRole_CannotUpdateAnotherTenantsOrderStatus()
    {
        // specs/restaurant/ordering/close-order.md is the first capability to UPDATE
        // restaurant.orders at all - CreateOrder only INSERTs it, and AddItem never touches it
        // (only restaurant.order_items). No prior test exercised UPDATE-side RLS on this table.
        var ownerTenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();

        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwtTokenFactory.CreateToken(ownerTenantId, Guid.NewGuid(), "restaurant.orders.create"));
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, ownerTenantId);
        var orderResponse = await client.PostAsJsonAsync("/api/v1/restaurants/orders", new { tableId });
        var orderId = (await orderResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderId").GetGuid();

        await using var connection = new NpgsqlConnection(fixture.AppRoleConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("SELECT set_config('app.tenant_id', @tenantId, false);", new { tenantId = otherTenantId.ToString() });

        // No "AND tenant_id = ..." on purpose: a genuine effect could only happen if RLS itself
        // were filtering, not application code. Unlike INSERT (which always attempts to create a
        // row and so throws outright on a WITH CHECK failure), the policy's USING clause makes
        // this row simply invisible to an UPDATE issued under the wrong tenant context - the
        // statement succeeds but silently affects zero rows, rather than throwing.
        var rowsAffected = await connection.ExecuteAsync(
            "UPDATE restaurant.orders SET status = 'Closed' WHERE id = @orderId;", new { orderId });
        Assert.Equal(0, rowsAffected);

        await connection.ExecuteAsync("SELECT set_config('app.tenant_id', @tenantId, false);", new { tenantId = ownerTenantId.ToString() });
        var status = await connection.ExecuteScalarAsync<string>(
            "SELECT status FROM restaurant.orders WHERE tenant_id = @ownerTenantId AND id = @orderId;", new { ownerTenantId, orderId });
        Assert.Equal("Open", status);
    }

    [Fact]
    public async Task AppRole_CannotInsertAnOrderItemForAnotherTenant()
    {
        // specs/restaurant/ordering/add-item.md's write-isolation requirement, for the second
        // write capability (after CreateTable) exercised against these policies. The row's own
        // tenant_id matches its order/menu-item foreign key targets (tenantA) so this fails on
        // RLS specifically, not a foreign-key violation.
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwtTokenFactory.CreateToken(tenantA, Guid.NewGuid(), "restaurant.orders.create"));
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantA);
        var orderResponse = await client.PostAsJsonAsync("/api/v1/restaurants/orders", new { tableId });
        var orderBody = await orderResponse.Content.ReadFromJsonAsync<JsonElement>();
        var orderId = orderBody.GetProperty("orderId").GetGuid();
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantA);

        await using var connection = new NpgsqlConnection(fixture.AppRoleConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("SELECT set_config('app.tenant_id', @tenantId, false);", new { tenantId = tenantB.ToString() });

        var exception = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
            """
            INSERT INTO restaurant.order_items (id, tenant_id, order_id, menu_item_id, quantity, unit_price_snapshot)
            VALUES (@id, @tenantA, @orderId, @menuItemId, 1, 5);
            """,
            new { id = Guid.NewGuid(), tenantA, orderId, menuItemId }));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }
}
