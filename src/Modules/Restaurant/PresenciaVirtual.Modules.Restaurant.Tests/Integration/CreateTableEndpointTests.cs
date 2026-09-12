using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace PresenciaVirtual.Modules.Restaurant.Tests.Integration;

[Collection(ApiCollection.Name)]
public class CreateTableEndpointTests(ApiFixture fixture)
{
    private const string Endpoint = "/api/v1/restaurants/tables";
    private const string CreatePermission = "restaurant.tables.create";

    [Fact]
    public async Task AC1_HappyPath_CreatesATableForTheCurrentTenant()
    {
        using var client = AuthenticatedClient(Guid.NewGuid(), CreatePermission);

        var response = await client.PostAsJsonAsync(Endpoint, new { label = "Table 5" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Table 5", body.GetProperty("label").GetString());
        Assert.NotEqual(Guid.Empty, body.GetProperty("tableId").GetGuid());
    }

    [Fact]
    public async Task AC2_EmptyLabel_ReturnsBadRequest()
    {
        using var client = AuthenticatedClient(Guid.NewGuid(), CreatePermission);

        var response = await client.PostAsJsonAsync(Endpoint, new { label = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AC3_LabelTooLong_ReturnsBadRequest()
    {
        using var client = AuthenticatedClient(Guid.NewGuid(), CreatePermission);

        var response = await client.PostAsJsonAsync(Endpoint, new { label = new string('a', 101) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AC4_MissingPermission_ReturnsForbidden()
    {
        using var client = AuthenticatedClient(Guid.NewGuid()); // no permissions granted

        var response = await client.PostAsJsonAsync(Endpoint, new { label = "Table 5" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AC5_Unauthenticated_ReturnsUnauthorized()
    {
        using var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync(Endpoint, new { label = "Table 5" });

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
