using PresenciaVirtual.Modules.Restaurant.Tables.CreateTable;

namespace PresenciaVirtual.Api.Endpoints.Restaurant;

public static class TableEndpoints
{
    public static IEndpointRouteBuilder MapRestaurantTableEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/restaurants/tables");

        group.MapPost("/", CreateTableAsync)
            .RequireAuthorization("restaurant.tables.create");

        return app;
    }

    private static async Task<IResult> CreateTableAsync(
        CreateTableRequest request,
        CreateTableHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new CreateTableCommand(request.Label);

        var errors = CreateTableValidator.Validate(command);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = [.. errors] });
        }

        var result = await handler.HandleAsync(command, cancellationToken);

        var response = new CreateTableResponse(result.TableId, result.Label);
        return Results.Created($"/api/v1/restaurants/tables/{result.TableId}", response);
    }

    private sealed record CreateTableRequest(string Label);

    private sealed record CreateTableResponse(Guid TableId, string Label);
}
