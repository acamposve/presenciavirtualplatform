using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace PresenciaVirtual.Modules.Restaurant.Tests.Integration;

/// <summary>
/// specs/restaurant/menu/get-menu-items.md, AC1-AC11, executed against the real API and database.
/// </summary>
[Collection(ApiCollection.Name)]
public class GetMenuItemsEndpointTests(ApiFixture fixture)
{
    private const string Endpoint = "/api/v1/restaurants/menu-items";
    private const string CreatePermission = "restaurant.menuitems.create";
    private const string ReadPermission = "restaurant.menuitems.read";

    [Fact]
    public async Task AC1_HappyPath_ReturnsTheMenusItemsAndPagingEnvelope()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreatePermission, ReadPermission);
        var cokeId = await CreateMenuItemAsync(client, "Coke", 3.50m);
        var beerId = await CreateMenuItemAsync(client, "Beer", 5m, isAlcoholic: true);

        var response = await client.GetAsync(Endpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.Equal(50, body.GetProperty("pageSize").GetInt32());
        Assert.Equal(2, body.GetProperty("totalCount").GetInt32());

        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);

        var coke = Assert.Single(items, i => i.GetProperty("menuItemId").GetGuid() == cokeId);
        Assert.Equal("Coke", coke.GetProperty("name").GetString());
        Assert.Equal(3.50m, coke.GetProperty("price").GetDecimal());
        Assert.False(coke.GetProperty("isAlcoholic").GetBoolean());
        Assert.NotEqual(default(DateTimeOffset), coke.GetProperty("createdAt").GetDateTimeOffset());

        var beer = Assert.Single(items, i => i.GetProperty("menuItemId").GetGuid() == beerId);
        Assert.True(beer.GetProperty("isAlcoholic").GetBoolean());

        // FR2/AC1: TenantId is never part of the response body.
        Assert.False(coke.TryGetProperty("tenantId", out _));
    }

    [Fact]
    public async Task AC2_EmptyMenu_ReturnsAnEmptyPageAndZeroCount()
    {
        using var client = AuthenticatedClient(Guid.NewGuid(), ReadPermission);

        var response = await client.GetAsync(Endpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(body.GetProperty("items").EnumerateArray());
        Assert.Equal(0, body.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task AC3_TenantScoping_ReturnsOnlyTheCallersItemsAndCount()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        using var clientA = AuthenticatedClient(tenantA, CreatePermission, ReadPermission);
        using var clientB = AuthenticatedClient(tenantB, CreatePermission, ReadPermission);
        var aItem1 = await CreateMenuItemAsync(clientA, "A Burger");
        var aItem2 = await CreateMenuItemAsync(clientA, "A Fries");
        var bItem = await CreateMenuItemAsync(clientB, "B Burger");

        var responseA = await clientA.GetAsync(Endpoint);
        var responseB = await clientB.GetAsync(Endpoint);

        var bodyA = await responseA.Content.ReadFromJsonAsync<JsonElement>();
        var bodyB = await responseB.Content.ReadFromJsonAsync<JsonElement>();
        var idsA = ItemsOf(bodyA).Select(i => i.GetProperty("menuItemId").GetGuid()).ToList();
        var idsB = ItemsOf(bodyB).Select(i => i.GetProperty("menuItemId").GetGuid()).ToList();

        Assert.Equal(2, bodyA.GetProperty("totalCount").GetInt32());
        Assert.Contains(aItem1, idsA);
        Assert.Contains(aItem2, idsA);
        Assert.DoesNotContain(bItem, idsA);
        Assert.Equal(new[] { bItem }, idsB);
        Assert.Equal(1, bodyB.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task AC4_Pagination_PagesAreDisjointAndTotalCountIsStable()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreatePermission, ReadPermission);
        var created = new HashSet<Guid>();
        foreach (var name in new[] { "Item A", "Item B", "Item C", "Item D", "Item E" })
        {
            created.Add(await CreateMenuItemAsync(client, name));
        }

        var page1 = await client.GetAsync($"{Endpoint}?page=1&pageSize=2");
        var page2 = await client.GetAsync($"{Endpoint}?page=2&pageSize=2");
        var page3 = await client.GetAsync($"{Endpoint}?page=3&pageSize=2");

        var body1 = await page1.Content.ReadFromJsonAsync<JsonElement>();
        var body2 = await page2.Content.ReadFromJsonAsync<JsonElement>();
        var body3 = await page3.Content.ReadFromJsonAsync<JsonElement>();

        var ids1 = ItemsOf(body1).Select(i => i.GetProperty("menuItemId").GetGuid()).ToList();
        var ids2 = ItemsOf(body2).Select(i => i.GetProperty("menuItemId").GetGuid()).ToList();
        var ids3 = ItemsOf(body3).Select(i => i.GetProperty("menuItemId").GetGuid()).ToList();

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
        await CreateMenuItemAsync(client, "Only Item");

        var response = await client.GetAsync($"{Endpoint}?page=99&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
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

        var response = await client.GetAsync(Endpoint);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.Equal(50, body.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task AC8_Ordering_IsCaseInsensitiveByNameAndStableAcrossRequests()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreatePermission, ReadPermission);
        await CreateMenuItemAsync(client, "b");
        await CreateMenuItemAsync(client, "A");
        await CreateMenuItemAsync(client, "a");

        var first = await client.GetAsync($"{Endpoint}?pageSize=200");
        var second = await client.GetAsync($"{Endpoint}?pageSize=200");

        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        var firstNames = ItemsOf(firstBody).Select(i => i.GetProperty("name").GetString()!.ToLowerInvariant()).ToList();
        var firstIds = ItemsOf(firstBody).Select(i => i.GetProperty("menuItemId").GetGuid()).ToList();
        var secondIds = ItemsOf(secondBody).Select(i => i.GetProperty("menuItemId").GetGuid()).ToList();

        Assert.Equal(new[] { "a", "a", "b" }, firstNames);
        Assert.Equal(firstIds, secondIds);
    }

    [Fact]
    public async Task AC9_MissingPermission_ReturnsForbidden()
    {
        using var client = AuthenticatedClient(Guid.NewGuid()); // no restaurant.menuitems.read

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
    public async Task AC11_ReadYourWrites_ANewMenuItemIsImmediatelyListed()
    {
        var tenantId = Guid.NewGuid();
        using var client = AuthenticatedClient(tenantId, CreatePermission, ReadPermission);

        var menuItemId = await CreateMenuItemAsync(client, "Fresh Item");
        var response = await client.GetAsync(Endpoint);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(menuItemId, ItemsOf(body).Select(i => i.GetProperty("menuItemId").GetGuid()));
    }

    private static IEnumerable<JsonElement> ItemsOf(JsonElement body) => body.GetProperty("items").EnumerateArray();

    private async Task<Guid> CreateMenuItemAsync(HttpClient client, string name, decimal price = 5m, bool isAlcoholic = false)
    {
        var response = await client.PostAsJsonAsync(Endpoint, new { name, price, isAlcoholic });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("menuItemId").GetGuid();
    }

    private HttpClient AuthenticatedClient(Guid tenantId, params string[] permissions)
    {
        var client = fixture.CreateClient();
        var token = TestJwtTokenFactory.CreateToken(tenantId, Guid.NewGuid(), permissions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
