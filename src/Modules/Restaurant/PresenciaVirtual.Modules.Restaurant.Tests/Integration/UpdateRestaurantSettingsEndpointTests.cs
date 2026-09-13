using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dapper;
using Npgsql;
using Xunit;

namespace PresenciaVirtual.Modules.Restaurant.Tests.Integration;

[Collection(ApiCollection.Name)]
public class UpdateRestaurantSettingsEndpointTests(ApiFixture fixture)
{
    private const string Endpoint = "/api/v1/restaurants/settings";
    private const string UpdatePermission = "restaurant.settings.update";
    private const string CreateOrderPermission = "restaurant.orders.create";
    private const string AddItemPermission = "restaurant.orders.additem";

    [Fact]
    public async Task AC1_HappyPath_FirstTime_CreatesTheSettingsRow()
    {
        using var client = AuthenticatedClient(Guid.NewGuid(), UpdatePermission);

        var response = await client.PutAsJsonAsync(Endpoint, new { maxAlcoholicItemQuantityPerLine = 5 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(5, body.GetProperty("maxAlcoholicItemQuantityPerLine").GetInt32());
    }

    [Fact]
    public async Task AC2_HappyPath_UpdatingAnExistingValue()
    {
        using var client = AuthenticatedClient(Guid.NewGuid(), UpdatePermission);
        await client.PutAsJsonAsync(Endpoint, new { maxAlcoholicItemQuantityPerLine = 5 });

        var response = await client.PutAsJsonAsync(Endpoint, new { maxAlcoholicItemQuantityPerLine = 10 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(10, body.GetProperty("maxAlcoholicItemQuantityPerLine").GetInt32());
    }

    [Fact]
    public async Task AC3_ClearingTheLimit_AllowsAnyQuantityAfterwards()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, UpdatePermission, CreateOrderPermission, AddItemPermission);
        await client.PutAsJsonAsync(Endpoint, new { maxAlcoholicItemQuantityPerLine = 5 });

        var clearResponse = await client.PutAsJsonAsync(Endpoint, new { maxAlcoholicItemQuantityPerLine = (int?)null });

        Assert.Equal(HttpStatusCode.OK, clearResponse.StatusCode);
        var body = await clearResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("maxAlcoholicItemQuantityPerLine").ValueKind);

        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId, isAlcoholic: true);
        var addItemResponse = await client.PostAsJsonAsync($"/api/v1/restaurants/orders/{orderId}/items", new { menuItemId, quantity = 50 });

        Assert.Equal(HttpStatusCode.OK, addItemResponse.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task AC4_InvalidValue_ReturnsBadRequest(int value)
    {
        using var client = AuthenticatedClient(Guid.NewGuid(), UpdatePermission);

        var response = await client.PutAsJsonAsync(Endpoint, new { maxAlcoholicItemQuantityPerLine = value });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AC4b_ValueExceedingTheMaximum_ReturnsBadRequest()
    {
        using var client = AuthenticatedClient(Guid.NewGuid(), UpdatePermission);

        var response = await client.PutAsync(Endpoint, RawJson("""{"maxAlcoholicItemQuantityPerLine": 9999999999}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AC4c_MissingField_ReturnsBadRequest()
    {
        using var client = AuthenticatedClient(Guid.NewGuid(), UpdatePermission);

        var response = await client.PutAsync(Endpoint, RawJson("{}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("5")]
    public async Task AC4c_NonObjectRequestBody_ReturnsBadRequest_NotServerError(string json)
    {
        // Regression test: JsonElement.TryGetProperty throws InvalidOperationException when the
        // root isn't a JSON object, which would otherwise surface as an unhandled 500.
        using var client = AuthenticatedClient(Guid.NewGuid(), UpdatePermission);

        var response = await client.PutAsync(Endpoint, RawJson(json));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("""{"maxAlcoholicItemQuantityPerLine": 5.5}""")]
    [InlineData("""{"maxAlcoholicItemQuantityPerLine": "abc"}""")]
    [InlineData("""{"maxAlcoholicItemQuantityPerLine": true}""")]
    public async Task AC4d_MalformedValue_ReturnsBadRequest(string json)
    {
        using var client = AuthenticatedClient(Guid.NewGuid(), UpdatePermission);

        var response = await client.PutAsync(Endpoint, RawJson(json));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AC4e_FirstTimeExplicitNull_CreatesARowWithNoLimit()
    {
        using var client = AuthenticatedClient(Guid.NewGuid(), UpdatePermission);

        var response = await client.PutAsJsonAsync(Endpoint, new { maxAlcoholicItemQuantityPerLine = (int?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("maxAlcoholicItemQuantityPerLine").ValueKind);
    }

    [Fact]
    public async Task AC5_MissingPermission_ReturnsForbidden()
    {
        using var client = AuthenticatedClient(Guid.NewGuid()); // no permissions granted

        var response = await client.PutAsJsonAsync(Endpoint, new { maxAlcoholicItemQuantityPerLine = 5 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AC6_Unauthenticated_ReturnsUnauthorized()
    {
        using var client = fixture.CreateClient();

        var response = await client.PutAsJsonAsync(Endpoint, new { maxAlcoholicItemQuantityPerLine = 5 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AC7_ImmediatelyObservedByAddItem()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, UpdatePermission, CreateOrderPermission, AddItemPermission);
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId, isAlcoholic: true);

        await client.PutAsJsonAsync(Endpoint, new { maxAlcoholicItemQuantityPerLine = 3 });

        var response = await client.PostAsJsonAsync($"/api/v1/restaurants/orders/{orderId}/items", new { menuItemId, quantity = 4 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AC8_RepeatingTheIdenticalRequest_IsIdempotent()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, UpdatePermission);

        var first = await client.PutAsJsonAsync(Endpoint, new { maxAlcoholicItemQuantityPerLine = 7 });
        var second = await client.PutAsJsonAsync(Endpoint, new { maxAlcoholicItemQuantityPerLine = 7 });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(7, secondBody.GetProperty("maxAlcoholicItemQuantityPerLine").GetInt32());

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("SELECT set_config('app.tenant_id', @tenantId, false);", new { tenantId = tenantId.ToString() });
        var rowCount = await connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM restaurant.settings WHERE tenant_id = @tenantId;", new { tenantId });
        Assert.Equal(1, rowCount);
    }

    [Fact]
    public async Task AC9_LoweringTheLimit_DoesNotRetroactivelyRejectAnExistingLine()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, UpdatePermission, CreateOrderPermission, AddItemPermission);
        await client.PutAsJsonAsync(Endpoint, new { maxAlcoholicItemQuantityPerLine = 5 });
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var orderId = await CreateOpenOrderAsync(client, tableId);
        var menuItemId = await TestMenuItemSeeder.SeedMenuItemAsync(fixture.ConnectionString, tenantId, isAlcoholic: true);
        await client.PostAsJsonAsync($"/api/v1/restaurants/orders/{orderId}/items", new { menuItemId, quantity = 5 });

        var lowerResponse = await client.PutAsJsonAsync(Endpoint, new { maxAlcoholicItemQuantityPerLine = 2 });
        Assert.Equal(HttpStatusCode.OK, lowerResponse.StatusCode);

        var quantityAfterLowering = await GetLineQuantityDirectlyAsync(tenantId, orderId, menuItemId);
        Assert.Equal(5, quantityAfterLowering);

        var furtherAddResponse = await client.PostAsJsonAsync($"/api/v1/restaurants/orders/{orderId}/items", new { menuItemId, quantity = 1 });
        Assert.Equal(HttpStatusCode.Conflict, furtherAddResponse.StatusCode);
    }

    private static HttpContent RawJson(string json) => new StringContent(json, Encoding.UTF8, "application/json");

    private async Task<Guid> CreateOpenOrderAsync(HttpClient client, Guid tableId)
    {
        var response = await client.PostAsJsonAsync("/api/v1/restaurants/orders", new { tableId });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("orderId").GetGuid();
    }

    private async Task<int> GetLineQuantityDirectlyAsync(Guid tenantId, Guid orderId, Guid menuItemId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("SELECT set_config('app.tenant_id', @tenantId, false);", new { tenantId = tenantId.ToString() });

        return await connection.ExecuteScalarAsync<int>(
            "SELECT quantity FROM restaurant.order_items WHERE tenant_id = @tenantId AND order_id = @orderId AND menu_item_id = @menuItemId;",
            new { tenantId, orderId, menuItemId });
    }

    private HttpClient AuthenticatedClient(Guid tenantId, params string[] permissions)
    {
        var client = fixture.CreateClient();
        var token = TestJwtTokenFactory.CreateToken(tenantId, Guid.NewGuid(), permissions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
