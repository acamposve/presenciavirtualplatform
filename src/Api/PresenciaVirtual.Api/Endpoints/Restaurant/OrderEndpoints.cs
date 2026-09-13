using Microsoft.AspNetCore.Mvc;
using PresenciaVirtual.Modules.Restaurant.Ordering.AddItem;
using PresenciaVirtual.Modules.Restaurant.Ordering.CloseOrder;
using PresenciaVirtual.Modules.Restaurant.Ordering.CreateOrder;
using PresenciaVirtual.Modules.Restaurant.Ordering.GetOrder;

namespace PresenciaVirtual.Api.Endpoints.Restaurant;

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapRestaurantOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/restaurants/orders");

        group.MapPost("/", CreateOrderAsync)
            .RequireAuthorization("restaurant.orders.create");

        group.MapGet("/", GetOrderAsync)
            .RequireAuthorization("restaurant.orders.read");

        group.MapPost("/{orderId:guid}/items", AddItemAsync)
            .RequireAuthorization("restaurant.orders.additem");

        group.MapPost("/{orderId:guid}/close", CloseOrderAsync)
            .RequireAuthorization("restaurant.orders.close");

        return app;
    }

    private static async Task<IResult> CreateOrderAsync(
        CreateOrderRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CreateOrderHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new CreateOrderCommand(request.TableId, idempotencyKey);

        var errors = CreateOrderValidator.Validate(command);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = [.. errors] });
        }

        try
        {
            var result = await handler.HandleAsync(command, cancellationToken);

            var response = new CreateOrderResponse(result.OrderId, result.TableId, result.Status.ToString(), result.CreatedAt);

            // AC7: a replayed request returns the original order (200), rather than 201, since
            // no new resource was created.
            return result.IsReplay
                ? Results.Ok(response)
                : Results.Created($"/api/v1/restaurants/orders/{result.OrderId}", response);
        }
        catch (TableNotFoundException)
        {
            return Results.NotFound();
        }
        catch (TableAlreadyHasOpenOrderException)
        {
            return Results.Conflict();
        }
        catch (IdempotencyKeyConflictException)
        {
            return Results.Conflict();
        }
    }

    private sealed record CreateOrderRequest(Guid TableId);

    private sealed record CreateOrderResponse(Guid OrderId, Guid TableId, string Status, DateTimeOffset CreatedAt);

    private static async Task<IResult> AddItemAsync(
        Guid orderId,
        AddItemRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        AddItemHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new AddItemCommand(orderId, request.MenuItemId, request.Quantity, idempotencyKey);

        var errors = AddItemValidator.Validate(command);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = [.. errors] });
        }

        try
        {
            var result = await handler.HandleAsync(command, cancellationToken);

            var response = new AddItemResponse(
                result.OrderId,
                [.. result.Items.Select(i => new AddItemResponseLine(i.MenuItemId, i.Quantity, i.UnitPriceSnapshot, i.LineTotal))],
                result.Total);

            return Results.Ok(response);
        }
        catch (OrderNotFoundException)
        {
            return Results.NotFound();
        }
        catch (MenuItemNotFoundException)
        {
            return Results.NotFound();
        }
        catch (OrderNotOpenException)
        {
            return Results.Conflict();
        }
        catch (AlcoholicItemLimitExceededException)
        {
            return Results.Conflict();
        }
        catch (LineQuantityTooLargeException)
        {
            return Results.Conflict();
        }
        catch (AddItemIdempotencyKeyConflictException)
        {
            return Results.Conflict();
        }
    }

    private sealed record AddItemRequest(Guid MenuItemId, int Quantity);

    private sealed record AddItemResponseLine(Guid MenuItemId, int Quantity, decimal UnitPriceSnapshot, decimal LineTotal);

    private sealed record AddItemResponse(Guid OrderId, IReadOnlyList<AddItemResponseLine> Items, decimal Total);

    private static async Task<IResult> GetOrderAsync(
        string? tableId,
        GetOrderHandler handler,
        CancellationToken cancellationToken)
    {
        // tableId is bound as a plain (nullable) query-string value, not a strongly-typed Guid
        // parameter, so that BOTH a missing value and a malformed one flow into
        // GetOrderValidator's "required" check (AC9's 400) instead of a framework binding
        // failure - a required {tableId:guid} route segment would 404 on a missing value before
        // the handler ever runs, which a prior review round correctly flagged as not satisfying
        // AC9's "missing TableId -> 400" requirement.
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
        catch (GetOrderTableNotFoundException)
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

    private static async Task<IResult> CloseOrderAsync(
        Guid orderId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CloseOrderHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new CloseOrderCommand(orderId, idempotencyKey);

        var errors = CloseOrderValidator.Validate(command);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = [.. errors] });
        }

        try
        {
            var result = await handler.HandleAsync(command, cancellationToken);

            var response = new CloseOrderResponse(
                result.OrderId,
                result.TableId,
                result.Status.ToString(),
                result.CreatedAt,
                [.. result.Items.Select(i => new CloseOrderResponseLine(i.MenuItemId, i.Quantity, i.UnitPriceSnapshot, i.LineTotal))],
                result.Total);

            return Results.Ok(response);
        }
        catch (CloseOrderNotFoundException)
        {
            return Results.NotFound();
        }
        catch (OrderAlreadyClosedException)
        {
            return Results.Conflict();
        }
        catch (CloseOrderIdempotencyKeyConflictException)
        {
            return Results.Conflict();
        }
    }

    private sealed record CloseOrderResponseLine(Guid MenuItemId, int Quantity, decimal UnitPriceSnapshot, decimal LineTotal);

    private sealed record CloseOrderResponse(Guid OrderId, Guid TableId, string Status, DateTimeOffset CreatedAt, IReadOnlyList<CloseOrderResponseLine> Items, decimal Total);
}
