using PresenciaVirtual.Modules.Restaurant.Paging;
using PresenciaVirtual.Modules.Restaurant.Tables.CreateTable;
using PresenciaVirtual.Modules.Restaurant.Tables.ListTables;

namespace PresenciaVirtual.Api.Endpoints.Restaurant;

public static class TableEndpoints
{
    public static IEndpointRouteBuilder MapRestaurantTableEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/restaurants/tables");

        group.MapPost("/", CreateTableAsync)
            .RequireAuthorization("restaurant.tables.create");

        group.MapGet("/", ListTablesAsync)
            .RequireAuthorization("restaurant.tables.read");

        return app;
    }

    private static async Task<IResult> ListTablesAsync(
        int? page,
        int? pageSize,
        ListTablesHandler handler,
        CancellationToken cancellationToken)
    {
        // FR5: each paging value defaults independently, so supplying only one is well defined.
        var query = new ListTablesQuery(PageRequest.ResolvePage(page), PageRequest.ResolvePageSize(pageSize));

        var errors = ListTablesValidator.Validate(query);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = [.. errors] });
        }

        var result = await handler.HandleAsync(query, cancellationToken);

        var response = new ListTablesResponse(
            [.. result.Items.Select(t => new ListTablesResponseItem(t.TableId, t.Label, t.CreatedAt))],
            result.Page,
            result.PageSize,
            result.TotalCount);

        return Results.Ok(response);
    }

    private sealed record ListTablesResponseItem(Guid TableId, string Label, DateTimeOffset CreatedAt);

    private sealed record ListTablesResponse(IReadOnlyList<ListTablesResponseItem> Items, int Page, int PageSize, int TotalCount);

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
