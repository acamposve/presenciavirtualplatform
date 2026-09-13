using PresenciaVirtual.Modules.Restaurant.Ordering.GetOrder;
using PresenciaVirtual.Modules.Restaurant.Tables.CreateTable;

namespace PresenciaVirtual.Api.Endpoints.Restaurant;

public static class TableEndpoints
{
    public static IEndpointRouteBuilder MapRestaurantTableEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/restaurants/tables");

        group.MapPost("/", CreateTableAsync)
            .RequireAuthorization("restaurant.tables.create");

        group.MapGet("/{tableId}/order", GetOrderAsync)
            .RequireAuthorization("restaurant.orders.read");

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

    private static async Task<IResult> GetOrderAsync(
        string tableId,
        GetOrderHandler handler,
        CancellationToken cancellationToken)
    {
        // tableId is bound as a plain string, not {tableId:guid}, so that a malformed value
        // reaches validation (AC9's 400) instead of a route mismatch (which would 404) - the
        // same "Guid.Empty triggers the 'required' validation error" pattern CreateOrderValidator
        // already uses for a missing body field.
        Guid.TryParse(tableId, out var parsedTableId);
        var query = new GetOrderQuery(parsedTableId);

        var errors = GetOrderValidator.Validate(query);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = [.. errors] });
        }

        try
        {
            var result = await handler.HandleAsync(query, cancellationToken);

            var response = new GetOrderResponse(
                result.OrderId,
                result.TableId,
                result.Status.ToString(),
                result.CreatedAt,
                [.. result.Items.Select(i => new GetOrderResponseLine(i.MenuItemId, i.Quantity, i.UnitPriceSnapshot, i.LineTotal))],
                result.Total);

            return Results.Ok(response);
        }
        catch (TableNotFoundException)
        {
            return Results.NotFound();
        }
        catch (TableHasNoOpenOrderException)
        {
            return Results.NotFound();
        }
    }

    private sealed record GetOrderResponseLine(Guid MenuItemId, int Quantity, decimal UnitPriceSnapshot, decimal LineTotal);

    private sealed record GetOrderResponse(Guid OrderId, Guid TableId, string Status, DateTimeOffset CreatedAt, IReadOnlyList<GetOrderResponseLine> Items, decimal Total);
}
