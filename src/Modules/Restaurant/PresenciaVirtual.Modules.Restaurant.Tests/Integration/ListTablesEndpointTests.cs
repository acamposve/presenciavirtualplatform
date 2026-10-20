using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace PresenciaVirtual.Modules.Restaurant.Tests.Integration;

/// <summary>
/// specs/restaurant/tables/list-tables.md, AC1-AC11, executed against the real API and database.
/// </summary>
[Collection(ApiCollection.Name)]
public class ListTablesEndpointTests(ApiFixture fixture)
{
    private const string Endpoint = "/api/v1/restaurants/tables";
    private const string CreatePermission = "restaurant.tables.create";
    private const string ReadPermission = "restaurant.tables.read";
    private const string CreateOrderPermission = "restaurant.orders.create";

    [Fact]
    public async Task AC1_HappyPath_ReturnsTheFloorsTablesAndPagingEnvelope()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreatePermission, ReadPermission);
        var table5 = await CreateTableAsync(client, "Table 5");
        var table12 = await CreateTableAsync(client, "Table 12");

        var response = await client.GetAsync(Endpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.Equal(50, body.GetProperty("pageSize").GetInt32());
        Assert.Equal(2, body.GetProperty("totalCount").GetInt32());

        var items = ItemsOf(body).ToList();
        var first = Assert.Single(items, t => t.GetProperty("tableId").GetGuid() == table5);
        Assert.Equal("Table 5", first.GetProperty("label").GetString());
        Assert.NotEqual(default(DateTimeOffset), first.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Single(items, t => t.GetProperty("tableId").GetGuid() == table12);

        // FR2/AC1: TenantId is never part of the response body.
        Assert.False(first.TryGetProperty("tenantId", out _));
    }

    [Fact]
    public async Task AC2_EmptyFloor_ReturnsAnEmptyPageAndZeroCount()
    {
        using var client = AuthenticatedClient(Guid.NewGuid(), ReadPermission);

        var response = await client.GetAsync(Endpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(body.GetProperty("items").EnumerateArray());
        Assert.Equal(0, body.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task AC3_TenantScoping_ReturnsOnlyTheCallersTablesAndCount()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        using var clientA = AuthenticatedClient(tenantA, CreatePermission, ReadPermission);
        using var clientB = AuthenticatedClient(tenantB, CreatePermission, ReadPermission);
        var aTable1 = await CreateTableAsync(clientA, "A1");
        var aTable2 = await CreateTableAsync(clientA, "A2");
        var bTable = await CreateTableAsync(clientB, "B1");

        var responseA = await clientA.GetAsync(Endpoint);
        var responseB = await clientB.GetAsync(Endpoint);

        var bodyA = await responseA.Content.ReadFromJsonAsync<JsonElement>();
        var bodyB = await responseB.Content.ReadFromJsonAsync<JsonElement>();
        var idsA = ItemsOf(bodyA).Select(t => t.GetProperty("tableId").GetGuid()).ToList();
        var idsB = ItemsOf(bodyB).Select(t => t.GetProperty("tableId").GetGuid()).ToList();

        Assert.Equal(2, bodyA.GetProperty("totalCount").GetInt32());
        Assert.Contains(aTable1, idsA);
        Assert.Contains(aTable2, idsA);
        Assert.DoesNotContain(bTable, idsA);
        Assert.Equal([bTable], idsB);
        Assert.Equal(1, bodyB.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task AC4_Pagination_PagesAreDisjointAndTotalCountIsStable()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreatePermission, ReadPermission);
        foreach (var label in new[] { "Table A", "Table B", "Table C", "Table D", "Table E" })
        {
            await CreateTableAsync(client, label);
        }

        var body1 = await GetBodyAsync(client, "?page=1&pageSize=2");
        var body2 = await GetBodyAsync(client, "?page=2&pageSize=2");
        var body3 = await GetBodyAsync(client, "?page=3&pageSize=2");

        var ids1 = ItemsOf(body1).Select(t => t.GetProperty("tableId").GetGuid()).ToList();
        var ids2 = ItemsOf(body2).Select(t => t.GetProperty("tableId").GetGuid()).ToList();
        var ids3 = ItemsOf(body3).Select(t => t.GetProperty("tableId").GetGuid()).ToList();

        Assert.Equal(2, ids1.Count);
        Assert.Equal(2, ids2.Count);
        Assert.Single(ids3);
        Assert.Empty(ids1.Intersect(ids2));
        Assert.Empty(ids2.Intersect(ids3));
        Assert.Equal(5, ids1.Concat(ids2).Concat(ids3).Distinct().Count());
        Assert.All(new[] { body1, body2, body3 }, b => Assert.Equal(5, b.GetProperty("totalCount").GetInt32()));
    }

    [Fact]
    public async Task AC5_PageBeyondTheEnd_ReturnsAnEmptyPageWithTheRealCount()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreatePermission, ReadPermission);
        await CreateTableAsync(client, "Only Table");

        var body = await GetBodyAsync(client, "?page=99&pageSize=10");

        Assert.Empty(body.GetProperty("items").EnumerateArray());
        Assert.Equal(1, body.GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?page=-1")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=201")]
    public async Task AC6_InvalidPagingValues_AreRejected(string queryString)
    {
        using var client = AuthenticatedClient(Guid.NewGuid(), ReadPermission);

        var response = await client.GetAsync(Endpoint + queryString);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AC6_NonNumericPagingValues_AreRejected()
    {
        using var client = AuthenticatedClient(Guid.NewGuid(), ReadPermission);

        var response = await client.GetAsync($"{Endpoint}?page=abc&pageSize=xyz");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AC6b_PageSizeAtTheCeiling_IsAccepted()
    {
        using var client = AuthenticatedClient(Guid.NewGuid(), ReadPermission);

        var response = await client.GetAsync($"{Endpoint}?pageSize=200");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(200, body.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task AC7_DefaultPaging_IsPageOneOfFifty()
    {
        using var client = AuthenticatedClient(Guid.NewGuid(), ReadPermission);

        var body = await GetBodyAsync(client, string.Empty);

        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.Equal(50, body.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task AC8_Ordering_IsCaseInsensitiveByLabelAndStableAcrossRequests()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreatePermission, ReadPermission);
        await CreateTableAsync(client, "b");
        await CreateTableAsync(client, "A");
        await CreateTableAsync(client, "a"); // duplicate of "A" once lowercased - the id tiebreaker decides

        var firstBody = await GetBodyAsync(client, "?pageSize=200");
        var secondBody = await GetBodyAsync(client, "?pageSize=200");

        var firstLabels = ItemsOf(firstBody).Select(t => t.GetProperty("label").GetString()!.ToLowerInvariant()).ToList();
        var firstIds = ItemsOf(firstBody).Select(t => t.GetProperty("tableId").GetGuid()).ToList();
        var secondIds = ItemsOf(secondBody).Select(t => t.GetProperty("tableId").GetGuid()).ToList();

        Assert.Equal(new[] { "a", "a", "b" }, firstLabels);
        Assert.Equal(firstIds, secondIds);
    }

    [Fact]
    public async Task AC9_MissingPermission_ReturnsForbidden()
    {
        using var client = AuthenticatedClient(Guid.NewGuid()); // no restaurant.tables.read

        var response = await client.GetAsync(Endpoint);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AC10_Unauthenticated_ReturnsUnauthorized()
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync(Endpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AC11_AListedTableIsImmediatelyUsableByCreateOrder()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreatePermission, ReadPermission, CreateOrderPermission);
        var tableId = await CreateTableAsync(client, "Table 7");

        var body = await GetBodyAsync(client, string.Empty);
        Assert.Contains(tableId, ItemsOf(body).Select(t => t.GetProperty("tableId").GetGuid()));

        var createOrderResponse = await client.PostAsJsonAsync("/api/v1/restaurants/orders", new { tableId });

        Assert.Equal(HttpStatusCode.Created, createOrderResponse.StatusCode);
    }

    private static IEnumerable<JsonElement> ItemsOf(JsonElement body) => body.GetProperty("items").EnumerateArray();

    private async Task<JsonElement> GetBodyAsync(HttpClient client, string queryString)
    {
        var response = await client.GetAsync(Endpoint + queryString);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<Guid> CreateTableAsync(HttpClient client, string label)
    {
        var response = await client.PostAsJsonAsync(Endpoint, new { label });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("tableId").GetGuid();
    }

    private HttpClient AuthenticatedClient(Guid tenantId, params string[] permissions)
    {
        var client = fixture.CreateClient();
        var token = TestJwtTokenFactory.CreateToken(tenantId, Guid.NewGuid(), permissions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
