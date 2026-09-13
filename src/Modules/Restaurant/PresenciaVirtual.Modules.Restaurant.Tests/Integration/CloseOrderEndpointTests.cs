using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Npgsql;
using Xunit;

namespace PresenciaVirtual.Modules.Restaurant.Tests.Integration;

[Collection(ApiCollection.Name)]
public class CloseOrderEndpointTests(ApiFixture fixture)
{
    private const string CreateOrderPermission = "restaurant.orders.create";
    private const string AddItemPermission = "restaurant.orders.additem";
    private const string ClosePermission = "restaurant.orders.close";

    [Fact]
    public async Task AC1_HappyPath_ClosesTheOrderAndReturnsItsFinalState()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission, ClosePermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId, price: 4m);
        await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 2 });

        var response = await client.PostAsync(CloseEndpoint(orderId), null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(orderId, body.GetProperty("orderId").GetGuid());
        Assert.Equal(tableId, body.GetProperty("tableId").GetGuid());
        Assert.Equal("Closed", body.GetProperty("status").GetString());
        var line = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal(menuItemId, line.GetProperty("menuItemId").GetGuid());
        Assert.Equal(8m, body.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task AC2_OrderNotFound_ReturnsNotFound()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, ClosePermission);

        var response = await client.PostAsync(CloseEndpoint(Guid.NewGuid()), null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AC2_MalformedOrderId_ReturnsNotFound()
    {
        // The {orderId:guid} route constraint is what makes this 404 (route mismatch) rather
        // than a 400 from model binding - see close-order.md's Error Scenarios note.
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, ClosePermission);

        var response = await client.PostAsync("/api/v1/restaurants/orders/not-a-guid/close", null);

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
        using var client = AuthenticatedClient(callerTenantId, ClosePermission);

        var response = await client.PostAsync(CloseEndpoint(orderId), null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AC3_OrderAlreadyClosed_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, ClosePermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);
        await client.PostAsync(CloseEndpoint(orderId), null);

        var response = await client.PostAsync(CloseEndpoint(orderId), null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AC4_MissingPermission_ReturnsForbidden()
    {
        var tenantId = Guid.NewGuid();
        using var creatorClient = AuthenticatedClient(tenantId, CreateOrderPermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(creatorClient, tableId);
        using var client = AuthenticatedClient(tenantId); // no restaurant.orders.close

        var response = await client.PostAsync(CloseEndpoint(orderId), null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AC5_Unauthenticated_ReturnsUnauthorized()
    {
        using var client = fixture.CreateClient();

        var response = await client.PostAsync(CloseEndpoint(Guid.NewGuid()), null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AC6_RepeatingTheSameIdempotencyKey_ReplaysWithoutReprocessing()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, ClosePermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);
        HttpRequestMessage Request() => new(HttpMethod.Post, CloseEndpoint(orderId)) { Headers = { { "Idempotency-Key", "close-key" } } };

        var first = await client.SendAsync(Request());
        var replay = await client.SendAsync(Request());

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayBody = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Closed", replayBody.GetProperty("status").GetString());
    }

    [Fact]
    public async Task AC7_ReusingTheIdempotencyKeyForADifferentOrder_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, ClosePermission);
        var tableA = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var tableB = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderA = await CreateOpenOrderAsync(client, tableA);
        var orderB = await CreateOpenOrderAsync(client, tableB);
        await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, CloseEndpoint(orderA)) { Headers = { { "Idempotency-Key", "shared-key" } } });

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, CloseEndpoint(orderB)) { Headers = { { "Idempotency-Key", "shared-key" } } });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AC7_ReusingTheIdempotencyKeyForANonexistentCrossTenantOrder_ReturnsConflictNotNotFound()
    {
        // BR3: the claim check MUST run before resolving the second OrderId at all, so this
        // returns 409 even though orderB - deliberately a different tenant's order, which is
        // also nonexistent from the caller's own perspective - would 404 on its own (AC2). A
        // regression that resolved the target first would return 404 here instead.
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, ClosePermission);
        var tableA = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderA = await CreateOpenOrderAsync(client, tableA);
        await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, CloseEndpoint(orderA)) { Headers = { { "Idempotency-Key", "cross-tenant-shared-key" } } });

        var otherTenantId = Guid.NewGuid();
        using var otherTenantClient = AuthenticatedClient(otherTenantId, CreateOrderPermission);
        var tableB = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, otherTenantId);
        var orderB = await CreateOpenOrderAsync(otherTenantClient, tableB);
        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, CloseEndpoint(orderB)) { Headers = { { "Idempotency-Key", "cross-tenant-shared-key" } } });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AC8_ClosingAnOrderWithNoItems_ReturnsEmptyItemsAndZeroTotal()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, ClosePermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);

        var response = await client.PostAsync(CloseEndpoint(orderId), null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(body.GetProperty("items").EnumerateArray());
        Assert.Equal(0m, body.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task AC9_ClosingFreesTheTable_ForANewCreateOrderRequest()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, ClosePermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);
        await client.PostAsync(CloseEndpoint(orderId), null);

        var response = await client.PostAsJsonAsync("/api/v1/restaurants/orders", new { tableId });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(orderId, body.GetProperty("orderId").GetGuid());
    }

    [Fact]
    public async Task AC9_ReusingTheOriginalCreateOrderIdempotencyKey_StillReplaysTheOriginalClosedOrder()
    {
        // create-order.md BR6: reusing the key from the order that just got closed must keep
        // replaying that same (now Closed) order - it is not a way to open a replacement.
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, ClosePermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/restaurants/orders")
        {
            Content = JsonContent.Create(new { tableId }),
            Headers = { { "Idempotency-Key", "create-key" } },
        };
        var createResponse = await client.SendAsync(createRequest);
        var orderId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderId").GetGuid();
        await client.PostAsync(CloseEndpoint(orderId), null);

        var replayRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/restaurants/orders")
        {
            Content = JsonContent.Create(new { tableId }),
            Headers = { { "Idempotency-Key", "create-key" } },
        };
        var replayResponse = await client.SendAsync(replayRequest);

        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        var replayBody = await replayResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(orderId, replayBody.GetProperty("orderId").GetGuid());
        Assert.Equal("Closed", replayBody.GetProperty("status").GetString());
    }

    [Fact]
    public async Task AC10_ClosedOrderRejectsANewAddItemCall_ButReplaysAPreCloseIdempotentOne()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission, ClosePermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId);
        var addItemRequest = new HttpRequestMessage(HttpMethod.Post, ItemsEndpoint(orderId))
        {
            Content = JsonContent.Create(new { menuItemId, quantity = 1 }),
            Headers = { { "Idempotency-Key", "additem-key" } },
        };
        await client.SendAsync(addItemRequest);
        await client.PostAsync(CloseEndpoint(orderId), null);

        var newItemResponse = await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 1 });
        var replayRequest = new HttpRequestMessage(HttpMethod.Post, ItemsEndpoint(orderId))
        {
            Content = JsonContent.Create(new { menuItemId, quantity = 1 }),
            Headers = { { "Idempotency-Key", "additem-key" } },
        };
        var replayResponse = await client.SendAsync(replayRequest);

        Assert.Equal(HttpStatusCode.Conflict, newItemResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
    }

    [Fact]
    public async Task AC11_ConcurrentCloseAttempts_OnlyOneSucceeds()
    {
        var tenantId = Guid.NewGuid();
        using var clientA = AuthenticatedClient(tenantId, CreateOrderPermission, ClosePermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(clientA, tableId);
        using var clientB = AuthenticatedClient(tenantId, ClosePermission);

        var responses = await Task.WhenAll(
            clientA.PostAsync(CloseEndpoint(orderId), null),
            clientB.PostAsync(CloseEndpoint(orderId), null));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task AC12_ConcurrentIdenticalIdempotencyKey_ClosesExactlyOnce()
    {
        var tenantId = Guid.NewGuid();
        using var clientA = AuthenticatedClient(tenantId, CreateOrderPermission, ClosePermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(clientA, tableId);
        using var clientB = AuthenticatedClient(tenantId, ClosePermission);
        HttpRequestMessage Request() => new(HttpMethod.Post, CloseEndpoint(orderId)) { Headers = { { "Idempotency-Key", "concurrent-close-key" } } };

        var responses = await Task.WhenAll(clientA.SendAsync(Request()), clientB.SendAsync(Request()));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact]
    public async Task AC13_RaceBetweenAddItemAndCloseOrder_NeverAddsAnItemAfterTheCloseCommits()
    {
        var tenantId = Guid.NewGuid();
        using var clientA = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission, ClosePermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(clientA, tableId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId);
        using var clientB = AuthenticatedClient(tenantId, ClosePermission);

        var responses = await Task.WhenAll(
            clientA.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 1 }),
            clientB.PostAsync(CloseEndpoint(orderId), null));
        var addItemResponse = responses[0];
        var closeResponse = responses[1];

        // Only one close request is ever sent in this test, so it is not itself part of a status
        // race and must always succeed; AddItem is the one whose outcome depends on interleaving.
        Assert.Equal(HttpStatusCode.OK, closeResponse.StatusCode);
        Assert.True(addItemResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict);

        var finalItems = await GetOrderItemsDirectlyAsync(tenantId, orderId);
        if (addItemResponse.StatusCode == HttpStatusCode.OK)
        {
            // AddItem's mutation committed before the close - the item must be present.
            Assert.Single(finalItems);
        }
        else
        {
            // The close committed first - AddItem must not have landed anything.
            Assert.Empty(finalItems);
        }
    }

    [Fact]
    public async Task AC13_AddItemCommittingBeforeClose_BothSucceedAndTheItemIsIncluded()
    {
        // Deterministic complement to the genuinely-concurrent test above: that one only proves
        // whichever interleaving the scheduler happens to produce on a given run, so it cannot by
        // itself guarantee this specific ordering (AddItem-then-close) is ever actually exercised.
        // Running the two calls sequentially instead pins this branch of AC13 down explicitly.
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission, ClosePermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId, price: 5m);

        var addItemResponse = await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 1 });
        var closeResponse = await client.PostAsync(CloseEndpoint(orderId), null);

        Assert.Equal(HttpStatusCode.OK, addItemResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, closeResponse.StatusCode);
        var closeBody = await closeResponse.Content.ReadFromJsonAsync<JsonElement>();
        var line = Assert.Single(closeBody.GetProperty("items").EnumerateArray());
        Assert.Equal(menuItemId, line.GetProperty("menuItemId").GetGuid());
    }

    [Fact]
    public async Task AC13_CloseCommittingBeforeAddItem_TheLaterAddItemIsRejected()
    {
        // Deterministic complement covering the other required ordering (close-then-AddItem).
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission, ClosePermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId);

        var closeResponse = await client.PostAsync(CloseEndpoint(orderId), null);
        var addItemResponse = await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 1 });

        Assert.Equal(HttpStatusCode.OK, closeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, addItemResponse.StatusCode);
        Assert.Empty(await GetOrderItemsDirectlyAsync(tenantId, orderId));
    }

    private static string CloseEndpoint(Guid orderId) => $"/api/v1/restaurants/orders/{orderId}/close";

    private static string ItemsEndpoint(Guid orderId) => $"/api/v1/restaurants/orders/{orderId}/items";

    private async Task<Guid> CreateOpenOrderAsync(HttpClient client, Guid tableId)
    {
        var response = await client.PostAsJsonAsync("/api/v1/restaurants/orders", new { tableId });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("orderId").GetGuid();
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
