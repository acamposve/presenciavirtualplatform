using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Npgsql;
using Xunit;

namespace PresenciaVirtual.Modules.Restaurant.Tests.Integration;

/// <summary>
/// specs/restaurant/ordering/cancel-order.md, AC1-AC15, executed against the real API and
/// database - the same shape as CloseOrderEndpointTests, with CancelOrder substituted and the
/// cancel-specific requirements (an order with items stays cancellable and untouched; a cancelled
/// order is terminal alongside Closed) added.
/// </summary>
[Collection(ApiCollection.Name)]
public class CancelOrderEndpointTests(ApiFixture fixture)
{
    private const string CreateOrderPermission = "restaurant.orders.create";
    private const string AddItemPermission = "restaurant.orders.additem";
    private const string ReadPermission = "restaurant.orders.read";
    private const string ClosePermission = "restaurant.orders.close";
    private const string CancelPermission = "restaurant.orders.cancel";

    [Fact]
    public async Task AC1_HappyPath_CancelsTheOrderAndReturnsItsFinalState()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, CancelPermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);

        var response = await client.PostAsync(CancelEndpoint(orderId), null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(orderId, body.GetProperty("orderId").GetGuid());
        Assert.Equal(tableId, body.GetProperty("tableId").GetGuid());
        Assert.Equal("Cancelled", body.GetProperty("status").GetString());
        Assert.Empty(body.GetProperty("items").EnumerateArray());
        Assert.Equal(0m, body.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task AC2_OrderNotFound_ReturnsNotFound()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CancelPermission);

        var response = await client.PostAsync(CancelEndpoint(Guid.NewGuid()), null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AC2_MalformedOrderId_ReturnsNotFound()
    {
        // The {orderId:guid} route constraint is what makes this 404 (route mismatch) rather
        // than a 400 from model binding - see cancel-order.md's Error Scenarios note.
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CancelPermission);

        var response = await client.PostAsync("/api/v1/restaurants/orders/not-a-guid/cancel", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AC2_OrderBelongingToAnotherTenant_ReturnsNotFound()
    {
        var ownerTenantId = Guid.NewGuid();
        using var ownerClient = AuthenticatedClient(ownerTenantId, CreateOrderPermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, ownerTenantId);
        var orderId = await CreateOpenOrderAsync(ownerClient, tableId);
        var callerTenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(callerTenantId, CancelPermission);

        var response = await client.PostAsync(CancelEndpoint(orderId), null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AC3_OrderAlreadyCancelled_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, CancelPermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);
        await client.PostAsync(CancelEndpoint(orderId), null);

        var response = await client.PostAsync(CancelEndpoint(orderId), null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AC4_OrderAlreadyClosed_ReturnsConflict()
    {
        // BR1: a cancelled-only capability must not be able to void a completed service, and the
        // reverse direction (CancelOrder then CloseOrder) is covered by AC14.
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, ClosePermission, CancelPermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);
        var closeResponse = await client.PostAsync($"/api/v1/restaurants/orders/{orderId}/close", null);
        Assert.Equal(HttpStatusCode.OK, closeResponse.StatusCode);

        var response = await client.PostAsync(CancelEndpoint(orderId), null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Closed", await GetOrderStatusDirectlyAsync(tenantId, orderId));
    }

    [Fact]
    public async Task AC5_MissingPermission_ReturnsForbidden()
    {
        var tenantId = Guid.NewGuid();
        using var creatorClient = AuthenticatedClient(tenantId, CreateOrderPermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(creatorClient, tableId);
        using var client = AuthenticatedClient(tenantId); // no restaurant.orders.cancel

        var response = await client.PostAsync(CancelEndpoint(orderId), null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AC6_Unauthenticated_ReturnsUnauthorized()
    {
        using var client = fixture.CreateClient();

        var response = await client.PostAsync(CancelEndpoint(Guid.NewGuid()), null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AC7_ReusingTheIdempotencyKeyForADifferentOrder_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, CancelPermission);
        var tableA = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var tableB = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderA = await CreateOpenOrderAsync(client, tableA);
        var orderB = await CreateOpenOrderAsync(client, tableB);
        await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, CancelEndpoint(orderA)) { Headers = { { "Idempotency-Key", "shared-key" } } });

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, CancelEndpoint(orderB)) { Headers = { { "Idempotency-Key", "shared-key" } } });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        // The conflicting reuse must not have cancelled the second order as a side effect.
        Assert.Equal("Open", await GetOrderStatusDirectlyAsync(tenantId, orderB));
    }

    [Fact]
    public async Task AC7_ReusingTheIdempotencyKeyForANonexistentCrossTenantOrder_ReturnsConflictNotNotFound()
    {
        // BR3: the claim check MUST run before resolving the second OrderId at all, so this
        // returns 409 even though orderB - deliberately a different tenant's order, which is also
        // nonexistent from the caller's own perspective - would 404 on its own (AC2).
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, CancelPermission);
        var tableA = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderA = await CreateOpenOrderAsync(client, tableA);
        await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, CancelEndpoint(orderA)) { Headers = { { "Idempotency-Key", "cross-tenant-shared-key" } } });

        var otherTenantId = Guid.NewGuid();
        using var otherTenantClient = AuthenticatedClient(otherTenantId, CreateOrderPermission);
        var tableB = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, otherTenantId);
        var orderB = await CreateOpenOrderAsync(otherTenantClient, tableB);
        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, CancelEndpoint(orderB)) { Headers = { { "Idempotency-Key", "cross-tenant-shared-key" } } });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AC8_RepeatingTheSameIdempotencyKey_ReplaysWithoutReprocessing()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, CancelPermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);
        HttpRequestMessage Request() => new(HttpMethod.Post, CancelEndpoint(orderId)) { Headers = { { "Idempotency-Key", "cancel-key" } } };

        var first = await client.SendAsync(Request());
        var replay = await client.SendAsync(Request());

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayBody = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Cancelled", replayBody.GetProperty("status").GetString());
        Assert.Equal("Cancelled", await GetOrderStatusDirectlyAsync(tenantId, orderId));
    }

    [Fact]
    public async Task AC9_CancellingAnOrderWithItems_SucceedsAndLeavesTheItemsUntouched()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission, CancelPermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId, price: 4m);
        await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 2 });

        var response = await client.PostAsync(CancelEndpoint(orderId), null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var line = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal(menuItemId, line.GetProperty("menuItemId").GetGuid());
        Assert.Equal(2, line.GetProperty("quantity").GetInt32());
        Assert.Equal(8m, body.GetProperty("total").GetDecimal());

        // BR2: the persisted OrderItem rows themselves must be untouched - cancelling is not a
        // deletion of the order or its history.
        var persistedItems = await GetOrderItemsDirectlyAsync(tenantId, orderId);
        var persisted = Assert.Single(persistedItems);
        Assert.Equal(menuItemId, persisted.MenuItemId);
        Assert.Equal(2, persisted.Quantity);
    }

    [Fact]
    public async Task AC10_CancellingFreesTheTable_ForANewCreateOrderRequest()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, CancelPermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);
        await client.PostAsync(CancelEndpoint(orderId), null);

        var response = await client.PostAsJsonAsync("/api/v1/restaurants/orders", new { tableId });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(orderId, body.GetProperty("orderId").GetGuid());
    }

    [Fact]
    public async Task AC11_CancelledOrderRejectsANewAddItemCall_ButReplaysAPreCancellationIdempotentOne()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission, CancelPermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId);
        await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, ItemsEndpoint(orderId))
        {
            Content = JsonContent.Create(new { menuItemId, quantity = 1 }),
            Headers = { { "Idempotency-Key", "additem-key" } },
        });
        await client.PostAsync(CancelEndpoint(orderId), null);

        var newItemResponse = await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 1 });
        var replayResponse = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, ItemsEndpoint(orderId))
        {
            Content = JsonContent.Create(new { menuItemId, quantity = 1 }),
            Headers = { { "Idempotency-Key", "additem-key" } },
        });

        Assert.Equal(HttpStatusCode.Conflict, newItemResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        Assert.Single(await GetOrderItemsDirectlyAsync(tenantId, orderId));
    }

    [Fact]
    public async Task AC12_ConcurrentCancelAttempts_OnlyOneSucceeds()
    {
        var tenantId = Guid.NewGuid();
        using var clientA = AuthenticatedClient(tenantId, CreateOrderPermission, CancelPermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(clientA, tableId);
        using var clientB = AuthenticatedClient(tenantId, CancelPermission);

        var responses = await Task.WhenAll(
            clientA.PostAsync(CancelEndpoint(orderId), null),
            clientB.PostAsync(CancelEndpoint(orderId), null));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("Cancelled", await GetOrderStatusDirectlyAsync(tenantId, orderId));
    }

    [Fact]
    public async Task AC13_ConcurrentIdenticalIdempotencyKey_CancelsExactlyOnce()
    {
        var tenantId = Guid.NewGuid();
        using var clientA = AuthenticatedClient(tenantId, CreateOrderPermission, CancelPermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(clientA, tableId);
        using var clientB = AuthenticatedClient(tenantId, CancelPermission);
        HttpRequestMessage Request() => new(HttpMethod.Post, CancelEndpoint(orderId)) { Headers = { { "Idempotency-Key", "concurrent-cancel-key" } } };

        var responses = await Task.WhenAll(clientA.SendAsync(Request()), clientB.SendAsync(Request()));

        // Neither is a conflict against the other: one transitions, the other replays (BR3).
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal("Cancelled", await GetOrderStatusDirectlyAsync(tenantId, orderId));
    }

    [Fact]
    public async Task AC14_RaceBetweenCancelOrderAndCloseOrder_ExactlyOneTransitionWins()
    {
        var tenantId = Guid.NewGuid();
        using var clientA = AuthenticatedClient(tenantId, CreateOrderPermission, CancelPermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(clientA, tableId);
        using var clientB = AuthenticatedClient(tenantId, ClosePermission);

        var responses = await Task.WhenAll(
            clientA.PostAsync(CancelEndpoint(orderId), null),
            clientB.PostAsync($"/api/v1/restaurants/orders/{orderId}/close", null));
        var cancelResponse = responses[0];
        var closeResponse = responses[1];

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);

        // The persisted status must be exactly the winner's terminal state - never both, and
        // never still Open because both requests assumed they had won.
        var expected = cancelResponse.StatusCode == HttpStatusCode.OK ? "Cancelled" : "Closed";
        Assert.Equal(expected, await GetOrderStatusDirectlyAsync(tenantId, orderId));
    }

    [Fact]
    public async Task AC14_CancellingBeforeClosing_MakesTheCloseConflict()
    {
        // Deterministic complement to the genuinely-concurrent race above, pinning down the
        // cancel-wins ordering that a scheduler run cannot be relied upon to produce.
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, CancelPermission, ClosePermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);

        var cancelResponse = await client.PostAsync(CancelEndpoint(orderId), null);
        var closeResponse = await client.PostAsync($"/api/v1/restaurants/orders/{orderId}/close", null);

        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, closeResponse.StatusCode);
        Assert.Equal("Cancelled", await GetOrderStatusDirectlyAsync(tenantId, orderId));
    }

    [Fact]
    public async Task AC14_ClosingBeforeCancelling_MakesTheCancelConflict()
    {
        // The other direction, mirroring AC4.
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, CancelPermission, ClosePermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);

        var closeResponse = await client.PostAsync($"/api/v1/restaurants/orders/{orderId}/close", null);
        var cancelResponse = await client.PostAsync(CancelEndpoint(orderId), null);

        Assert.Equal(HttpStatusCode.OK, closeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, cancelResponse.StatusCode);
        Assert.Equal("Closed", await GetOrderStatusDirectlyAsync(tenantId, orderId));
    }

    [Fact]
    public async Task AC15_RaceBetweenAddItemAndCancelOrder_NeverAddsAnItemAfterTheCancellationCommits()
    {
        var tenantId = Guid.NewGuid();
        using var clientA = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission, CancelPermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(clientA, tableId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId);
        using var clientB = AuthenticatedClient(tenantId, CancelPermission);

        var responses = await Task.WhenAll(
            clientA.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 1 }),
            clientB.PostAsync(CancelEndpoint(orderId), null));
        var addItemResponse = responses[0];
        var cancelResponse = responses[1];

        // Only one cancel request is ever sent, so it is not itself part of a status race and
        // must always succeed; AddItem is the one whose outcome depends on interleaving.
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        Assert.True(addItemResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict);

        var finalItems = await GetOrderItemsDirectlyAsync(tenantId, orderId);
        if (addItemResponse.StatusCode == HttpStatusCode.OK)
        {
            // AddItem's mutation committed before the cancellation - the item stays in the
            // cancelled order's history (BR2).
            Assert.Single(finalItems);
        }
        else
        {
            Assert.Empty(finalItems);
        }

        Assert.Equal("Cancelled", await GetOrderStatusDirectlyAsync(tenantId, orderId));
    }

    [Fact]
    public async Task AC15_AddItemCommittingBeforeCancel_BothSucceedAndTheItemIsIncluded()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission, CancelPermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId, price: 5m);

        var addItemResponse = await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 1 });
        var cancelResponse = await client.PostAsync(CancelEndpoint(orderId), null);

        Assert.Equal(HttpStatusCode.OK, addItemResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        var cancelBody = await cancelResponse.Content.ReadFromJsonAsync<JsonElement>();
        var line = Assert.Single(cancelBody.GetProperty("items").EnumerateArray());
        Assert.Equal(menuItemId, line.GetProperty("menuItemId").GetGuid());
    }

    [Fact]
    public async Task AC15_CancelCommittingBeforeAddItem_TheLaterAddItemIsRejected()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission, CancelPermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId);

        var cancelResponse = await client.PostAsync(CancelEndpoint(orderId), null);
        var addItemResponse = await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 1 });

        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, addItemResponse.StatusCode);
        Assert.Empty(await GetOrderItemsDirectlyAsync(tenantId, orderId));
    }

    [Fact]
    public async Task GetOrder_NoLongerReturnsACancelledOrder_ConsistentlyWithAClosedOne()
    {
        // cancel-order.md's Consistency requirement: the cancelled status must be immediately
        // visible to a subsequent read, the same way get-order.md FR3 already treats Closed.
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, ReadPermission, CancelPermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);
        await client.PostAsync(CancelEndpoint(orderId), null);

        var response = await client.GetAsync($"/api/v1/restaurants/orders?tableId={tableId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static string CancelEndpoint(Guid orderId) => $"/api/v1/restaurants/orders/{orderId}/cancel";

    private static string ItemsEndpoint(Guid orderId) => $"/api/v1/restaurants/orders/{orderId}/items";

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
