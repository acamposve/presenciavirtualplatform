using PresenciaVirtual.Modules.Restaurant.Menu.CreateMenuItem;

namespace PresenciaVirtual.Api.Endpoints.Restaurant;

public static class MenuItemEndpoints
{
    public static IEndpointRouteBuilder MapRestaurantMenuItemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/restaurants/menu-items");

        group.MapPost("/", CreateMenuItemAsync)
            .RequireAuthorization("restaurant.menuitems.create");

        return app;
    }

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
