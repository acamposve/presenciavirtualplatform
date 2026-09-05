using Microsoft.AspNetCore.Mvc;
using PresenciaVirtual.Modules.Restaurant.Ordering.CreateOrder;

namespace PresenciaVirtual.Api.Endpoints.Restaurant;

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapRestaurantOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/restaurants/orders");

        group.MapPost("/", CreateOrderAsync)
            .RequireAuthorization("restaurant.orders.create");

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
            return Results.ValidationProblem(errors.ToDictionary(_ => "request", e => new[] { e }));
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
}
