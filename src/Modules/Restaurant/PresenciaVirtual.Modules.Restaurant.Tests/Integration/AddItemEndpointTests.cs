using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Npgsql;
using Xunit;

namespace PresenciaVirtual.Modules.Restaurant.Tests.Integration;

[Collection(ApiCollection.Name)]
public class AddItemEndpointTests(ApiFixture fixture)
{
    private const string CreateOrderPermission = "restaurant.orders.create";
    private const string AddItemPermission = "restaurant.orders.additem";

    [Fact]
    public async Task AC1_HappyPath_AddsAnItemAndReturnsCurrentOrderState()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission);
        var orderId = await CreateOpenOrderAsync(client, tenantId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId, price: 3m);

        var response = await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 2 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();
        var line = Assert.Single(items);
        Assert.Equal(menuItemId, line.GetProperty("menuItemId").GetGuid());
        Assert.Equal(2, line.GetProperty("quantity").GetInt32());
        Assert.Equal(3m, line.GetProperty("unitPriceSnapshot").GetDecimal());
        Assert.Equal(6m, line.GetProperty("lineTotal").GetDecimal());
        Assert.Equal(6m, body.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task AC2_OrderNotFound_ReturnsNotFound()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, AddItemPermission);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId);

        var response = await client.PostAsJsonAsync(ItemsEndpoint(Guid.NewGuid()), new { menuItemId, quantity = 1 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AC2_OrderBelongingToAnotherTenant_ReturnsNotFound()
    {
        var ownerTenantId = Guid.NewGuid();
        using var ownerClient = AuthenticatedClient(ownerTenantId, CreateOrderPermission);
        var orderId = await CreateOpenOrderAsync(ownerClient, ownerTenantId);
        var callerTenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(callerTenantId, AddItemPermission);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, callerTenantId);

        var response = await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 1 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AC4_MenuItemNotFound_ReturnsNotFound()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission);
        var orderId = await CreateOpenOrderAsync(client, tenantId);

        var response = await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId = Guid.NewGuid(), quantity = 1 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AC4_MenuItemBelongingToAnotherTenant_ReturnsNotFound()
    {
        var ownerTenantId = Guid.NewGuid();
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, ownerTenantId);
        var callerTenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(callerTenantId, CreateOrderPermission, AddItemPermission);
        var orderId = await CreateOpenOrderAsync(client, callerTenantId);

        var response = await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 1 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AC5_InvalidQuantity_ReturnsBadRequest()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission);
        var orderId = await CreateOpenOrderAsync(client, tenantId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId);

        var response = await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AC6_MissingPermission_ReturnsForbidden()
    {
        var tenantId = Guid.NewGuid();
        using var creatorClient = AuthenticatedClient(tenantId, CreateOrderPermission);
        var orderId = await CreateOpenOrderAsync(creatorClient, tenantId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId);
        using var client = AuthenticatedClient(tenantId); // no restaurant.orders.additem

        var response = await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 1 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AC7_Unauthenticated_ReturnsUnauthorized()
    {
        using var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync(ItemsEndpoint(Guid.NewGuid()), new { menuItemId = Guid.NewGuid(), quantity = 1 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AC8_RepeatingTheSameIdempotencyKey_DoesNotAddTheItemAgain()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission);
        var orderId = await CreateOpenOrderAsync(client, tenantId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId, price: 4m);
        HttpRequestMessage Request() => new(HttpMethod.Post, ItemsEndpoint(orderId))
        {
            Content = JsonContent.Create(new { menuItemId, quantity = 2 }),
            Headers = { { "Idempotency-Key", "replay-key" } },
        };

        var first = await client.SendAsync(Request());
        var replay = await client.SendAsync(Request());

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayBody = await replay.Content.ReadFromJsonAsync<JsonElement>();
        var line = Assert.Single(replayBody.GetProperty("items").EnumerateArray());
        Assert.Equal(2, line.GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task AC9_ReusingTheIdempotencyKeyForADifferentQuantity_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission);
        var orderId = await CreateOpenOrderAsync(client, tenantId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId);

        var first = new HttpRequestMessage(HttpMethod.Post, ItemsEndpoint(orderId))
        {
            Content = JsonContent.Create(new { menuItemId, quantity = 1 }),
            Headers = { { "Idempotency-Key", "conflict-key" } },
        };
        await client.SendAsync(first);

        var second = new HttpRequestMessage(HttpMethod.Post, ItemsEndpoint(orderId))
        {
            Content = JsonContent.Create(new { menuItemId, quantity = 2 }),
            Headers = { { "Idempotency-Key", "conflict-key" } },
        };
        var response = await client.SendAsync(second);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AC10_MergingDoesNotRepriceTheLine_EvenIfTheMenuItemsPriceLaterChanges()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission);
        var orderId = await CreateOpenOrderAsync(client, tenantId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId, price: 10m);
        await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 1 });

        // No MenuItem update capability exists yet (out of scope) - simulate the price change
        // directly, the same way seeding stands in for a missing Menu Management capability.
        await UpdateMenuItemPriceAsync(menuItemId, 12m);

        var response = await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 1 });

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var line = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal(2, line.GetProperty("quantity").GetInt32());
        Assert.Equal(10m, line.GetProperty("unitPriceSnapshot").GetDecimal());
        Assert.Equal(20m, line.GetProperty("lineTotal").GetDecimal());
    }

    [Fact]
    public async Task AC11_AddingTheSameMenuItemTwice_MergesIntoOneLine()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission);
        var orderId = await CreateOpenOrderAsync(client, tenantId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId);

        await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 1 });
        var response = await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 1 });

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var line = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal(2, line.GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task AC12_AddingDifferentMenuItems_KeepsSeparateLines()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission);
        var orderId = await CreateOpenOrderAsync(client, tenantId);
        var menuItemA = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId);
        var menuItemB = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId);

        await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId = menuItemA, quantity = 1 });
        var response = await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId = menuItemB, quantity = 1 });

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("items").EnumerateArray().Count());
    }

    [Fact]
    public async Task AC13_AlcoholicItemWithinTheConfiguredLimit_Succeeds()
    {
        var tenantId = Guid.NewGuid();
        await TestRestaurantSettingsSeeder.SeedMaxAlcoholicItemQuantityPerLineAsync(fixture.ConnectionString, tenantId, limit: 5);
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission);
        var orderId = await CreateOpenOrderAsync(client, tenantId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId, isAlcoholic: true);

        var response = await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 5 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AC14_AlcoholicItemOverTheConfiguredLimit_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        await TestRestaurantSettingsSeeder.SeedMaxAlcoholicItemQuantityPerLineAsync(fixture.ConnectionString, tenantId, limit: 5);
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission);
        var orderId = await CreateOpenOrderAsync(client, tenantId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId, isAlcoholic: true);
        await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 5 });

        var response = await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 1 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AC15_NoConfiguredLimit_AllowsAnyQuantity()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission);
        var orderId = await CreateOpenOrderAsync(client, tenantId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId, isAlcoholic: true);

        var response = await client.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 50 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Concurrency_SameMenuItemAddedTwiceAtTheSameTime_BothAreReflectedInTheFinalQuantity()
    {
        var tenantId = Guid.NewGuid();
        using var clientA = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission);
        var orderId = await CreateOpenOrderAsync(clientA, tenantId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId, price: 2m);
        using var clientB = AuthenticatedClient(tenantId, AddItemPermission);

        var responses = await Task.WhenAll(
            clientA.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 1 }),
            clientB.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 1 }));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        // Each response reflects current state as of its own request's completion (BR6/FR7) -
        // whichever of the two commits first will still only show its own addition, since the
        // other's commit may land a moment later. That is expected, not a lost update: BR4 only
        // guarantees the underlying quantity eventually reflects both, so verify that with a
        // fresh read after both requests have finished, not by trusting either individual
        // response body.
        var finalItems = await GetOrderItemsDirectlyAsync(tenantId, orderId);
        var line = Assert.Single(finalItems);
        Assert.Equal(2, line.Quantity);
        // add-item.md's concurrent-merge requirement also covers the order's Total, not just
        // the line's quantity - both must reflect both requests.
        Assert.Equal(4m, finalItems.Sum(i => i.Quantity * i.UnitPriceSnapshot));
    }

    [Fact]
    public async Task Concurrency_SameIdempotencyKeyAtTheSameTime_AddsTheItemExactlyOnce()
    {
        var tenantId = Guid.NewGuid();
        using var clientA = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission);
        var orderId = await CreateOpenOrderAsync(clientA, tenantId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId);
        using var clientB = AuthenticatedClient(tenantId, AddItemPermission);
        HttpRequestMessage Request() => new(HttpMethod.Post, ItemsEndpoint(orderId))
        {
            Content = JsonContent.Create(new { menuItemId, quantity = 1 }),
            Headers = { { "Idempotency-Key", "concurrent-key" } },
        };

        var responses = await Task.WhenAll(clientA.SendAsync(Request()), clientB.SendAsync(Request()));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var body = await responses[0].Content.ReadFromJsonAsync<JsonElement>();
        var line = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal(1, line.GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task Concurrency_TwoRequestsThatWouldTogetherExceedTheAlcoholicLimit_OnlyOneSucceeds()
    {
        var tenantId = Guid.NewGuid();
        await TestRestaurantSettingsSeeder.SeedMaxAlcoholicItemQuantityPerLineAsync(fixture.ConnectionString, tenantId, limit: 5);
        using var clientA = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission);
        var orderId = await CreateOpenOrderAsync(clientA, tenantId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId, isAlcoholic: true);
        await clientA.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 4 });
        using var clientB = AuthenticatedClient(tenantId, AddItemPermission);

        var responses = await Task.WhenAll(
            clientA.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 1 }),
            clientB.PostAsJsonAsync(ItemsEndpoint(orderId), new { menuItemId, quantity = 1 }));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
    }

    private static string ItemsEndpoint(Guid orderId) => $"/api/v1/restaurants/orders/{orderId}/items";

    private async Task<Guid> CreateOpenOrderAsync(HttpClient client, Guid tenantId)
    {
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var response = await client.PostAsJsonAsync("/api/v1/restaurants/orders", new { tableId });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("orderId").GetGuid();
    }

    private async Task<IReadOnlyList<(Guid MenuItemId, int Quantity, decimal UnitPriceSnapshot)>> GetOrderItemsDirectlyAsync(Guid tenantId, Guid orderId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("SELECT set_config('app.tenant_id', @tenantId, false);", new { tenantId = tenantId.ToString() });

        var rows = await connection.QueryAsync<(Guid MenuItemId, int Quantity, decimal UnitPriceSnapshot)>(
            "SELECT menu_item_id AS MenuItemId, quantity AS Quantity, unit_price_snapshot AS UnitPriceSnapshot FROM restaurant.order_items WHERE tenant_id = @tenantId AND order_id = @orderId;",
            new { tenantId, orderId });

        return rows.ToList();
    }

    private async Task UpdateMenuItemPriceAsync(Guid menuItemId, decimal newPrice)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("UPDATE restaurant.menu_items SET price = @newPrice WHERE id = @menuItemId;", new { menuItemId, newPrice });
    }

    private HttpClient AuthenticatedClient(Guid tenantId, params string[] permissions)
    {
        var client = fixture.CreateClient();
        var token = TestJwtTokenFactory.CreateToken(tenantId, Guid.NewGuid(), permissions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
