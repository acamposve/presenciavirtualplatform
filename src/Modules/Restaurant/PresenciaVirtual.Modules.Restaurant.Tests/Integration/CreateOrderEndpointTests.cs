using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace PresenciaVirtual.Modules.Restaurant.Tests.Integration;

public class CreateOrderEndpointTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const string Endpoint = "/api/v1/restaurants/orders";
    private const string CreatePermission = "restaurant.orders.create";

    [Fact]
    public async Task AC1_HappyPath_CreatesAnOpenOrderForTheTable()
    {
        var tenantId = Guid.NewGuid();
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        using var client = AuthenticatedClient(tenantId, Guid.NewGuid(), CreatePermission);

        var response = await client.PostAsJsonAsync(Endpoint, new { tableId });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(tableId, body.GetProperty("tableId").GetGuid());
        Assert.Equal("Open", body.GetProperty("status").GetString());
        Assert.NotEqual(Guid.Empty, body.GetProperty("orderId").GetGuid());
    }

    [Fact]
    public async Task AC2_TableAlreadyHasAnOpenOrder_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        using var client = AuthenticatedClient(tenantId, Guid.NewGuid(), CreatePermission);

        var first = await client.PostAsJsonAsync(Endpoint, new { tableId });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync(Endpoint, new { tableId });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task AC3_TableBelongsToADifferentTenant_ReturnsNotFound()
    {
        var ownerTenantId = Guid.NewGuid();
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, ownerTenantId);
        var callerTenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(callerTenantId, Guid.NewGuid(), CreatePermission);

        var response = await client.PostAsJsonAsync(Endpoint, new { tableId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AC4_MissingPermission_ReturnsForbidden()
    {
        var tenantId = Guid.NewGuid();
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        using var client = AuthenticatedClient(tenantId, Guid.NewGuid()); // no permissions granted

        var response = await client.PostAsJsonAsync(Endpoint, new { tableId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AC5_Unauthenticated_ReturnsUnauthorized()
    {
        using var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync(Endpoint, new { tableId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AC6_TableDoesNotExist_ReturnsNotFound()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, Guid.NewGuid(), CreatePermission);

        var response = await client.PostAsJsonAsync(Endpoint, new { tableId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AC7_RepeatingTheSameIdempotencyKey_ReturnsTheOriginalOrder()
    {
        var tenantId = Guid.NewGuid();
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        using var client = AuthenticatedClient(tenantId, Guid.NewGuid(), CreatePermission);
        var request = () => new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new { tableId }),
            Headers = { { "Idempotency-Key", "replay-key" } },
        };

        var first = await client.SendAsync(request());
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();

        var replay = await client.SendAsync(request());

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayBody = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(firstBody.GetProperty("orderId").GetGuid(), replayBody.GetProperty("orderId").GetGuid());
    }

    [Fact]
    public async Task AC8_ReusingTheIdempotencyKeyForADifferentTable_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        var tableA = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        var tableB = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        using var client = AuthenticatedClient(tenantId, Guid.NewGuid(), CreatePermission);

        var first = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new { tableId = tableA }),
            Headers = { { "Idempotency-Key", "conflict-key" } },
        };
        await client.SendAsync(first);

        var second = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new { tableId = tableB }),
            Headers = { { "Idempotency-Key", "conflict-key" } },
        };
        var response = await client.SendAsync(second);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Concurrency_OnlyOneOfTwoSimultaneousRequestsForTheSameTableSucceeds()
    {
        var tenantId = Guid.NewGuid();
        var tableId = await TestTableSeeder.SeedTableAsync(fixture.ConnectionString, tenantId);
        using var clientA = AuthenticatedClient(tenantId, Guid.NewGuid(), CreatePermission);
        using var clientB = AuthenticatedClient(tenantId, Guid.NewGuid(), CreatePermission);

        var responses = await Task.WhenAll(
            clientA.PostAsJsonAsync(Endpoint, new { tableId }),
            clientB.PostAsJsonAsync(Endpoint, new { tableId }));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
    }

    private HttpClient AuthenticatedClient(Guid tenantId, Guid userId, params string[] permissions)
    {
        var client = fixture.CreateClient();
        var token = TestJwtTokenFactory.CreateToken(tenantId, userId, permissions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
