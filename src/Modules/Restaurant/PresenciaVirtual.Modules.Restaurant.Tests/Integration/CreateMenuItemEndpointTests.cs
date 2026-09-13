using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace PresenciaVirtual.Modules.Restaurant.Tests.Integration;

[Collection(ApiCollection.Name)]
public class CreateMenuItemEndpointTests(ApiFixture fixture)
{
    private const string Endpoint = "/api/v1/restaurants/menu-items";
    private const string CreatePermission = "restaurant.menuitems.create";

    [Fact]
    public async Task AC1_HappyPath_CreatesAMenuItemForTheCurrentTenant()
    {
        using var client = AuthenticatedClient(Guid.NewGuid(), CreatePermission);

        var response = await client.PostAsJsonAsync(Endpoint, new { name = "Coke", price = 3.50m, isAlcoholic = false });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Coke", body.GetProperty("name").GetString());
        Assert.Equal(3.50m, body.GetProperty("price").GetDecimal());
        Assert.False(body.GetProperty("isAlcoholic").GetBoolean());
        Assert.NotEqual(Guid.Empty, body.GetProperty("menuItemId").GetGuid());
    }

    [Fact]
    public async Task AC1_IsAlcoholicOmitted_DefaultsToFalse()
    {
        using var client = AuthenticatedClient(Guid.NewGuid(), CreatePermission);

        var response = await client.PostAsJsonAsync(Endpoint, new { name = "Water", price = 1m });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("isAlcoholic").GetBoolean());
    }

    [Fact]
    public async Task AC2_AlcoholicItem_ReturnsIsAlcoholicTrue()
    {
        using var client = AuthenticatedClient(Guid.NewGuid(), CreatePermission);

        var response = await client.PostAsJsonAsync(Endpoint, new { name = "Beer", price = 5m, isAlcoholic = true });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("isAlcoholic").GetBoolean());
    }

    [Fact]
    public async Task AC3_EmptyName_ReturnsBadRequest()
    {
        using var client = AuthenticatedClient(Guid.NewGuid(), CreatePermission);

        var response = await client.PostAsJsonAsync(Endpoint, new { name = "   ", price = 3m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AC4_NameTooLong_ReturnsBadRequest()
    {
        using var client = AuthenticatedClient(Guid.NewGuid(), CreatePermission);

        var response = await client.PostAsJsonAsync(Endpoint, new { name = new string('a', 201), price = 3m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task AC5_InvalidPrice_ReturnsBadRequest(decimal price)
    {
        using var client = AuthenticatedClient(Guid.NewGuid(), CreatePermission);

        var response = await client.PostAsJsonAsync(Endpoint, new { name = "Coke", price });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AC5b_PriceWithExcessPrecision_ReturnsBadRequest()
    {
        using var client = AuthenticatedClient(Guid.NewGuid(), CreatePermission);

        var response = await client.PostAsJsonAsync(Endpoint, new { name = "Coke", price = 1.999m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AC5c_PriceExceedingTheMaximum_ReturnsBadRequest()
    {
        using var client = AuthenticatedClient(Guid.NewGuid(), CreatePermission);

        var response = await client.PostAsJsonAsync(Endpoint, new { name = "Coke", price = 100_000_000.00m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AC6_MissingPermission_ReturnsForbidden()
    {
        using var client = AuthenticatedClient(Guid.NewGuid()); // no permissions granted

        var response = await client.PostAsJsonAsync(Endpoint, new { name = "Coke", price = 3m });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AC7_Unauthenticated_ReturnsUnauthorized()
    {
        using var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync(Endpoint, new { name = "Coke", price = 3m });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private HttpClient AuthenticatedClient(Guid tenantId, params string[] permissions)
    {
        var client = fixture.CreateClient();
        var token = TestJwtTokenFactory.CreateToken(tenantId, Guid.NewGuid(), permissions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
