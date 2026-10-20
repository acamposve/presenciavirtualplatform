using PresenciaVirtual.Modules.Restaurant.Menu.GetMenuItems;
using PresenciaVirtual.Modules.Restaurant.Menu.CreateMenuItem;
using PresenciaVirtual.Modules.Restaurant.Paging;

namespace PresenciaVirtual.Api.Endpoints.Restaurant;

public static class MenuItemEndpoints
{
    public static IEndpointRouteBuilder MapRestaurantMenuItemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/restaurants/menu-items");

        group.MapPost("/", CreateMenuItemAsync)
            .RequireAuthorization("restaurant.menuitems.create");

        group.MapGet("/", GetMenuItemsAsync)
            .RequireAuthorization("restaurant.menuitems.read");

        return app;
    }

    private static async Task<IResult> GetMenuItemsAsync(
        int? page,
        int? pageSize,
        GetMenuItemsHandler handler,
        CancellationToken cancellationToken)
    {
        // FR5: each paging value defaults independently, so supplying only one is well defined.
        var query = new GetMenuItemsQuery(PageRequest.ResolvePage(page), PageRequest.ResolvePageSize(pageSize));

        var errors = GetMenuItemsValidator.Validate(query);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = [.. errors] });
        }

        var result = await handler.HandleAsync(query, cancellationToken);

        var response = new GetMenuItemsResponse(
            [.. result.Items.Select(i => new GetMenuItemsResponseItem(i.MenuItemId, i.Name, i.Price, i.IsAlcoholic, i.CreatedAt))],
            result.Page,
            result.PageSize,
            result.TotalCount);

        return Results.Ok(response);
    }

    private sealed record GetMenuItemsResponseItem(Guid MenuItemId, string Name, decimal Price, bool IsAlcoholic, DateTimeOffset CreatedAt);

    private sealed record GetMenuItemsResponse(IReadOnlyList<GetMenuItemsResponseItem> Items, int Page, int PageSize, int TotalCount);

    private static async Task<IResult> CreateMenuItemAsync(
        CreateMenuItemRequest request,
        CreateMenuItemHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new CreateMenuItemCommand(request.Name, request.Price, request.IsAlcoholic ?? false);

        var errors = CreateMenuItemValidator.Validate(command);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = [.. errors] });
        }

        var result = await handler.HandleAsync(command, cancellationToken);

        var response = new CreateMenuItemResponse(result.MenuItemId, result.Name, result.Price, result.IsAlcoholic);
        return Results.Created($"/api/v1/restaurants/menu-items/{result.MenuItemId}", response);
    }

    private sealed record CreateMenuItemRequest(string Name, decimal Price, bool? IsAlcoholic);

    private sealed record CreateMenuItemResponse(Guid MenuItemId, string Name, decimal Price, bool IsAlcoholic);
}
