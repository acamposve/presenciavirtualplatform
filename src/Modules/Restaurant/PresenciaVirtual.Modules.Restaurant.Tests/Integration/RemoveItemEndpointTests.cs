using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Npgsql;
using Xunit;

namespace PresenciaVirtual.Modules.Restaurant.Tests.Integration;

/// <summary>
/// specs/restaurant/ordering/remove-item.md, AC1-AC15, executed against the real API and
/// database — the same shape as CancelOrderEndpointTests, with RemoveItem's own rules
/// (whole-line deletion while the order stays Open; the guarded delete; the AddItem race in both
/// directions) substituted.
/// </summary>
[Collection(ApiCollection.Name)]
public class RemoveItemEndpointTests(ApiFixture fixture)
{
    private const string CreateOrderPermission = "restaurant.orders.create";
    private const string AddItemPermission = "restaurant.orders.additem";
    private const string ReadPermission = "restaurant.orders.read";
    private const string ClosePermission = "restaurant.orders.close";
    private const string CancelPermission = "restaurant.orders.cancel";
    private const string RemovePermission = "restaurant.orders.removeitem";

    [Fact]
    public async Task AC1_HappyPath_RemovesTheLineAndReturnsTheRemainingState()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission, RemovePermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);
        var keptItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId, price: 3m);
        var removedItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId, price: 4m);
        await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId = keptItemId, quantity = 1 });
        await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId = removedItemId, quantity = 2 });

        var response = await client.DeleteAsync(RemoveEndpoint(orderId, removedItemId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(orderId, body.GetProperty("orderId").GetGuid());
        var line = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal(keptItemId, line.GetProperty("menuItemId").GetGuid());
        Assert.Equal(3m, body.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task AC2_OrderNotFound_ReturnsNotFound()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, RemovePermission);

        var response = await client.DeleteAsync(RemoveEndpoint(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AC2_OrderBelongingToAnotherTenant_ReturnsNotFoundAndLeavesTheLineIntact()
    {
        var ownerTenantId = Guid.NewGuid();
        using var ownerClient = AuthenticatedClient(ownerTenantId, CreateOrderPermission, AddItemPermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, ownerTenantId);
        var orderId = await CreateOpenOrderAsync(ownerClient, tableId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, ownerTenantId);
        await ownerClient.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 1 });
        var callerTenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(callerTenantId, RemovePermission);

        var response = await client.DeleteAsync(RemoveEndpoint(orderId, menuItemId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Single(await GetOrderItemsDirectlyAsync(ownerTenantId, orderId));
    }

    [Fact]
    public async Task AC3_ClosedOrder_ReturnsConflictAndLeavesTheLineInPlace()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission, ClosePermission, RemovePermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId);
        await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 1 });
        var closeResponse = await client.PostAsync($"/api/v1/restaurants/orders/{orderId}/close", null);
        Assert.Equal(HttpStatusCode.OK, closeResponse.StatusCode);

        var response = await client.DeleteAsync(RemoveEndpoint(orderId, menuItemId));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Single(await GetOrderItemsDirectlyAsync(tenantId, orderId));
    }

    [Fact]
    public async Task AC3_CancelledOrder_ReturnsConflictAndLeavesTheLineInPlace()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission, CancelPermission, RemovePermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId);
        await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 1 });
        var cancelResponse = await client.PostAsync($"/api/v1/restaurants/orders/{orderId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);

        var response = await client.DeleteAsync(RemoveEndpoint(orderId, menuItemId));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Single(await GetOrderItemsDirectlyAsync(tenantId, orderId));
    }

    [Fact]
    public async Task AC4_LineNeverAdded_ReturnsNotFound()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, RemovePermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);

        var response = await client.DeleteAsync(RemoveEndpoint(orderId, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AC4_RemovingTheSameLineTwice_ReturnsNotFoundTheSecondTime()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission, RemovePermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId);
        await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 1 });

        var first = await client.DeleteAsync(RemoveEndpoint(orderId, menuItemId));
        var second = await client.DeleteAsync(RemoveEndpoint(orderId, menuItemId));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
    }

    [Fact]
    public async Task AC5_MalformedIdentifiers_ReturnNotFound()
    {
        // Both {orderId:guid} and {menuItemId:guid} are route constraints, so a malformed value is
        // a route mismatch (404) rather than a 400 from model binding.
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, RemovePermission);

        var malformedOrderId = await client.DeleteAsync($"/api/v1/restaurants/orders/not-a-guid/items/{Guid.NewGuid()}");
        var malformedMenuItemId = await client.DeleteAsync($"/api/v1/restaurants/orders/{Guid.NewGuid()}/items/not-a-guid");

        Assert.Equal(HttpStatusCode.NotFound, malformedOrderId.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, malformedMenuItemId.StatusCode);
    }

    [Fact]
    public async Task AC6_MissingPermission_ReturnsForbiddenAndLeavesTheLineInPlace()
    {
        var tenantId = Guid.NewGuid();
        using var ownerClient = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(ownerClient, tableId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId);
        await ownerClient.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 1 });
        using var client = AuthenticatedClient(tenantId); // no restaurant.orders.removeitem

        var response = await client.DeleteAsync(RemoveEndpoint(orderId, menuItemId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Single(await GetOrderItemsDirectlyAsync(tenantId, orderId));
    }

    [Fact]
    public async Task AC7_Unauthenticated_ReturnsUnauthorized()
    {
        using var client = fixture.CreateClient();

        var response = await client.DeleteAsync(RemoveEndpoint(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AC8_ReusingTheIdempotencyKeyForADifferentMenuItem_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission, RemovePermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);
        var itemA = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId);
        var itemB = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId);
        await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId = itemA, quantity = 1 });
        await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId = itemB, quantity = 1 });
        await client.SendAsync(RemoveRequest(orderId, itemA, "shared-remove-key"));

        var response = await client.SendAsync(RemoveRequest(orderId, itemB, "shared-remove-key"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        // The conflicting reuse must not have removed the second line as a side effect.
        var remaining = await GetOrderItemsDirectlyAsync(tenantId, orderId);
        Assert.Single(remaining);
        Assert.Equal(itemB, remaining[0].MenuItemId);
    }

    [Fact]
    public async Task AC8_ReusingTheIdempotencyKeyForANonexistentCrossTenantOrder_ReturnsConflictNotNotFound()
    {
        // BR6: the claim check MUST run before resolving the order at all, so this returns 409
        // even though the second order belongs to another tenant and would 404 on its own (AC2).
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission, RemovePermission);
        var tableA = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderA = await CreateOpenOrderAsync(client, tableA);
        var itemA = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId);
        await client.PostAsJsonAsync(ItemsEndpoint(orderA), new { menuItemId = itemA, quantity = 1 });
        await client.SendAsync(RemoveRequest(orderA, itemA, "cross-tenant-remove-key"));

        var otherTenantId = Guid.NewGuid();
        using var otherTenantClient = AuthenticatedClient(otherTenantId, CreateOrderPermission);
        var tableB = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, otherTenantId);
        var orderB = await CreateOpenOrderAsync(otherTenantClient, tableB);
        var response = await client.SendAsync(RemoveRequest(orderB, Guid.NewGuid(), "cross-tenant-remove-key"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AC9_RepeatingTheSameIdempotencyKey_ReplaysInsteadOfReportingNotFound()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission, RemovePermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId);
        await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 1 });

        var first = await client.SendAsync(RemoveRequest(orderId, menuItemId, "remove-key"));
        var replay = await client.SendAsync(RemoveRequest(orderId, menuItemId, "remove-key"));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        // Without the key this would be a 404 (AC4), since the line is already gone.
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayBody = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(replayBody.GetProperty("items").EnumerateArray());
        Assert.Equal(0m, replayBody.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task AC10_RemovingTheLastLine_LeavesAnEmptyOpenOrder()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission, ReadPermission, RemovePermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId, price: 4m);
        await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 2 });

        var response = await client.DeleteAsync(RemoveEndpoint(orderId, menuItemId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(body.GetProperty("items").EnumerateArray());
        Assert.Equal(0m, body.GetProperty("total").GetDecimal());
        // Removal never changes the order's own status: it is still Open and still findable.
        Assert.Equal("Open", await GetOrderStatusDirectlyAsync(tenantId, orderId));
        var getOrderResponse = await client.GetAsync($"/api/v1/restaurants/orders?tableId={tableId}");
        Assert.Equal(HttpStatusCode.OK, getOrderResponse.StatusCode);
    }

    [Fact]
    public async Task AC11_RemovingOneLine_LeavesTheOtherLinesPriceAndQuantityUntouched()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission, RemovePermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);
        var survivorId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId, price: 4m);
        var removedId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId, price: 7.5m);
        await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId = survivorId, quantity = 3 });
        await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId = removedId, quantity = 2 });

        var response = await client.DeleteAsync(RemoveEndpoint(orderId, removedId));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var line = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal(survivorId, line.GetProperty("menuItemId").GetGuid());
        Assert.Equal(3, line.GetProperty("quantity").GetInt32());
        Assert.Equal(4m, line.GetProperty("unitPriceSnapshot").GetDecimal());
        Assert.Equal(12m, body.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task AC12_ConcurrentRemovalsOfTheSameLine_ExactlyOneSucceeds()
    {
        var tenantId = Guid.NewGuid();
        using var clientA = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission, RemovePermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(clientA, tableId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId);
        await clientA.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 1 });
        using var clientB = AuthenticatedClient(tenantId, RemovePermission);

        var responses = await Task.WhenAll(
            clientA.DeleteAsync(RemoveEndpoint(orderId, menuItemId)),
            clientB.DeleteAsync(RemoveEndpoint(orderId, menuItemId)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.NotFound);
        Assert.Empty(await GetOrderItemsDirectlyAsync(tenantId, orderId));
    }

    [Fact]
    public async Task AC13_RaceBetweenRemoveItemAndCloseOrder_ExactlyOneOutcomePerInterleaving()
    {
        var tenantId = Guid.NewGuid();
        using var clientA = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission, RemovePermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(clientA, tableId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId);
        await clientA.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 1 });
        using var clientB = AuthenticatedClient(tenantId, ClosePermission);

        var responses = await Task.WhenAll(
            clientA.DeleteAsync(RemoveEndpoint(orderId, menuItemId)),
            clientB.PostAsync($"/api/v1/restaurants/orders/{orderId}/close", null));
        var removeResponse = responses[0];
        var closeResponse = responses[1];

        // Only one close request is sent, so it always succeeds; the removal may win (200, line
        // gone) or lose to the transition (409, line still present).
        Assert.Equal(HttpStatusCode.OK, closeResponse.StatusCode);
        Assert.True(removeResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict);
        Assert.Equal("Closed", await GetOrderStatusDirectlyAsync(tenantId, orderId));
        var finalItems = await GetOrderItemsDirectlyAsync(tenantId, orderId);
        if (removeResponse.StatusCode == HttpStatusCode.OK)
        {
            Assert.Empty(finalItems);
        }
        else
        {
            Assert.Single(finalItems);
        }
    }

    [Fact]
    public async Task AC13_RemovingBeforeClosing_ClosesWithoutThatLine()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission, ClosePermission, RemovePermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId);
        await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 1 });

        var removeResponse = await client.DeleteAsync(RemoveEndpoint(orderId, menuItemId));
        var closeResponse = await client.PostAsync($"/api/v1/restaurants/orders/{orderId}/close", null);

        Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, closeResponse.StatusCode);
        var closeBody = await closeResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(closeBody.GetProperty("items").EnumerateArray());
        Assert.Equal(0m, closeBody.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task AC13_ClosingBeforeRemoving_MakesTheRemovalConflict()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission, ClosePermission, RemovePermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId);
        await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 1 });

        var closeResponse = await client.PostAsync($"/api/v1/restaurants/orders/{orderId}/close", null);
        var removeResponse = await client.DeleteAsync(RemoveEndpoint(orderId, menuItemId));

        Assert.Equal(HttpStatusCode.OK, closeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, removeResponse.StatusCode);
    }

    [Fact]
    public async Task AC14_RaceBetweenAddItemAndRemoveItemForTheSameMenu_NeverTearsTheLine()
    {
        // Both interleavings are legitimate, and in both *both* requests succeed: add-then-remove
        // leaves no line (the addition was merged, then deleted); remove-then-add leaves a fresh
        // line at the added quantity (a new snapshot). What must never happen is a half-applied
        // line or a quantity that belongs to neither ordering.
        var tenantId = Guid.NewGuid();
        using var clientA = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission, RemovePermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(clientA, tableId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId);
        await clientA.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 1 });
        using var clientB = AuthenticatedClient(tenantId, RemovePermission);

        var responses = await Task.WhenAll(
            clientA.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 1 }),
            clientB.DeleteAsync(RemoveEndpoint(orderId, menuItemId)));

        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);

        var finalItems = await GetOrderItemsDirectlyAsync(tenantId, orderId);
        Assert.True(finalItems.Count is 0 or 1, $"unexpected line count {finalItems.Count}");
        if (finalItems.Count == 1)
        {
            // remove-then-add: the line was recreated from scratch at the added quantity.
            Assert.Equal(1, finalItems[0].Quantity);
        }
    }

    [Fact]
    public async Task AC14_RemovingBeforeAdding_MakesTheLaterAddCreateAFreshLine()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission, RemovePermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId);
        await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 1 });

        var removeResponse = await client.DeleteAsync(RemoveEndpoint(orderId, menuItemId));
        var addResponse = await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 2 });

        Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);
        var line = Assert.Single(await GetOrderItemsDirectlyAsync(tenantId, orderId));
        Assert.Equal(2, line.Quantity);
    }

    [Fact]
    public async Task AC14_AddingBeforeRemoving_RemovesTheMergedLine()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission, RemovePermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId);
        await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 1 });

        var addResponse = await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 1 });
        var removeResponse = await client.DeleteAsync(RemoveEndpoint(orderId, menuItemId));

        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);
        Assert.Empty(await GetOrderItemsDirectlyAsync(tenantId, orderId));
    }

    [Fact]
    public async Task AC15_RemovingALineFromAnotherTenantsOrder_ReturnsNotFound()
    {
        // AC15 is the cross-tenant counterpart of AC2/AC4 at the line level: the caller's own
        // tenant is the one whose order is looked up, so an order id from another tenant is simply
        // not found rather than forbidden.
        var ownerTenantId = Guid.NewGuid();
        var callerTenantId = Guid.NewGuid();
        using var ownerClient = AuthenticatedClient(ownerTenantId, CreateOrderPermission, AddItemPermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, ownerTenantId);
        var orderId = await CreateOpenOrderAsync(ownerClient, tableId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, ownerTenantId);
        await ownerClient.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 1 });
        using var callerClient = AuthenticatedClient(callerTenantId, RemovePermission);

        var response = await callerClient.DeleteAsync(RemoveEndpoint(orderId, menuItemId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Single(await GetOrderItemsDirectlyAsync(ownerTenantId, orderId));
    }

    private static string RemoveEndpoint(Guid orderId, Guid menuItemId) => $"/api/v1/restaurants/orders/{orderId}/items/{menuItemId}";

    private static string ItemsEndpoint(Guid orderId) => $"/api/v1/restaurants/orders/{orderId}/items";

    private static HttpRequestMessage RemoveRequest(Guid orderId, Guid menuItemId, string idempotencyKey)
        => new(HttpMethod.Delete, RemoveEndpoint(orderId, menuItemId)) { Headers = { { "Idempotency-Key", idempotencyKey } } };

    private async Task<Guid> CreateOpenOrderAsync(HttpClient client, Guid tableId)
    {
        var response = await client.PostAsJsonAsync("/api/v1/restaurants/orders", new { tableId });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("orderId").GetGuid();
    }

    private async Task<string> GetOrderStatusDirectlyAsync(Guid tenantId, Guid orderId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("SELECT set_config('app.tenant_id', @tenantId, false);", new { tenantId = tenantId.ToString() });

        var status = await connection.ExecuteScalarAsync<string>(
            "SELECT status FROM restaurant.orders WHERE tenant_id = @tenantId AND id = @orderId;", new { tenantId, orderId });

        return status ?? string.Empty;
    }

    private async Task<IReadOnlyList<(Guid MenuItemId, int Quantity)>> GetOrderItemsDirectlyAsync(Guid tenantId, Guid orderId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("SELECT set_config('app.tenant_id', @tenantId, false);", new { tenantId = tenantId.ToString() });

        var rows = await connection.QueryAsync<(Guid MenuItemId, int Quantity)>(
            "SELECT menu_item_id AS MenuItemId, quantity AS Quantity FROM restaurant.order_items WHERE tenant_id = @tenantId AND order_id = @orderId;",
            new { tenantId, orderId });

        return rows.ToList();
    }

    private HttpClient AuthenticatedClient(Guid tenantId, params string[] permissions)
    {
        var client = fixture.CreateClient();
        var token = TestJwtTokenFactory.CreateToken(tenantId, Guid.NewGuid(), permissions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
