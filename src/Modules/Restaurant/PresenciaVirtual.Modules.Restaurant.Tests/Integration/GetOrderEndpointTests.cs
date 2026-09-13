using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace PresenciaVirtual.Modules.Restaurant.Tests.Integration;

[Collection(ApiCollection.Name)]
public class GetOrderEndpointTests(ApiFixture fixture)
{
    private const string CreateOrderPermission = "restaurant.orders.create";
    private const string AddItemPermission = "restaurant.orders.additem";
    private const string GetOrderPermission = "restaurant.orders.read";

    [Fact]
    public async Task AC1_HappyPath_ReturnsTheTablesOpenOrder()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission, GetOrderPermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId, price: 3m);
        await client.PostAsJsonAsync($"/api/v1/restaurants/orders/{orderId}/items", new { menuItemId, quantity = 2 });

        var response = await client.GetAsync(Endpoint(tableId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(orderId, body.GetProperty("orderId").GetGuid());
        Assert.Equal(tableId, body.GetProperty("tableId").GetGuid());
        Assert.Equal("Open", body.GetProperty("status").GetString());
        Assert.True(body.TryGetProperty("createdAt", out _));
        var line = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal(menuItemId, line.GetProperty("menuItemId").GetGuid());
        Assert.Equal(2, line.GetProperty("quantity").GetInt32());
        Assert.Equal(3m, line.GetProperty("unitPriceSnapshot").GetDecimal());
        Assert.Equal(6m, line.GetProperty("lineTotal").GetDecimal());
        Assert.Equal(6m, body.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task AC2_TableWithNoOpenOrder_ReturnsNotFound()
    {
        var tenantId = Guid.NewGuid();
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        using var client = AuthenticatedClient(tenantId, GetOrderPermission);

        var response = await client.GetAsync(Endpoint(tableId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AC3_TableDoesNotExist_ReturnsNotFound()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, GetOrderPermission);

        var response = await client.GetAsync(Endpoint(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AC4_TableBelongsToADifferentTenant_ReturnsNotFound()
    {
        var ownerTenantId = Guid.NewGuid();
        using var ownerClient = AuthenticatedClient(ownerTenantId, CreateOrderPermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, ownerTenantId);
        await CreateOpenOrderAsync(ownerClient, tableId);
        var callerTenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(callerTenantId, GetOrderPermission);

        var response = await client.GetAsync(Endpoint(tableId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AC5_MissingPermission_ReturnsForbidden()
    {
        var tenantId = Guid.NewGuid();
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        using var client = AuthenticatedClient(tenantId); // no restaurant.orders.read

        var response = await client.GetAsync(Endpoint(tableId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AC6_Unauthenticated_ReturnsUnauthorized()
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync(Endpoint(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AC7_ReflectsItemsAddedAfterTheOrderWasCreated()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, AddItemPermission, GetOrderPermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);

        var beforeResponse = await client.GetAsync(Endpoint(tableId));
        var beforeBody = await beforeResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(beforeBody.GetProperty("items").EnumerateArray());

        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId, price: 5m);
        await client.PostAsJsonAsync($"/api/v1/restaurants/orders/{orderId}/items", new { menuItemId, quantity = 1 });

        var afterResponse = await client.GetAsync(Endpoint(tableId));
        var afterBody = await afterResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Single(afterBody.GetProperty("items").EnumerateArray());
        Assert.Equal(5m, afterBody.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task AC8_EmptyOrder_ReturnsAnEmptyItemListAndZeroTotal()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreateOrderPermission, GetOrderPermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        await CreateOpenOrderAsync(client, tableId);

        var response = await client.GetAsync(Endpoint(tableId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(body.GetProperty("items").EnumerateArray());
        Assert.Equal(0m, body.GetProperty("total").GetDecimal());
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task AC9_MissingOrMalformedTableId_ReturnsBadRequest(string rawTableId)
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, GetOrderPermission);

        var response = await client.GetAsync($"/api/v1/restaurants/tables/{rawTableId}/order");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static string Endpoint(Guid tableId) => $"/api/v1/restaurants/tables/{tableId}/order";

    private async Task<Guid> CreateOpenOrderAsync(HttpClient client, Guid tableId)
    {
        var response = await client.PostAsJsonAsync("/api/v1/restaurants/orders", new { tableId });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("orderId").GetGuid();
    }

    private HttpClient AuthenticatedClient(Guid tenantId, params string[] permissions)
    {
        var client = fixture.CreateClient();
        var token = TestJwtTokenFactory.CreateToken(tenantId, Guid.NewGuid(), permissions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
