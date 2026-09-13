using System.Text.Json;
using PresenciaVirtual.Modules.Restaurant.Settings.UpdateRestaurantSettings;

namespace PresenciaVirtual.Api.Endpoints.Restaurant;

public static class RestaurantSettingsEndpoints
{
    private const string FieldName = "maxAlcoholicItemQuantityPerLine";

    public static IEndpointRouteBuilder MapRestaurantSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/restaurants/settings");

        group.MapPut("/", UpdateRestaurantSettingsAsync)
            .RequireAuthorization("restaurant.settings.update");

        return app;
    }

    private static async Task<IResult> UpdateRestaurantSettingsAsync(
        HttpRequest request,
        UpdateRestaurantSettingsHandler handler,
        CancellationToken cancellationToken)
    {
        // A plain `int?` request DTO cannot distinguish an omitted field from an explicit `null`
        // (System.Text.Json binds both to C# null), but clearing the limit is a destructive
        // action that must only ever happen deliberately (AC4c) - so the body is parsed manually
        // here instead of through automatic model binding.
        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = ["Request body must be valid JSON."] });
        }

        using (document)
        {
            // TryGetProperty throws InvalidOperationException if the root isn't a JSON object
            // (e.g. the body is `null`, `[]`, or a bare number) - checking the root kind first
            // turns that case into the same 400 as any other malformed body, instead of an
            // unhandled 500.
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = ["Request body must be a JSON object."] });
            }

            if (!document.RootElement.TryGetProperty(FieldName, out var valueElement))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = [$"{FieldName} is required."] });
            }

            int? maxAlcoholicItemQuantityPerLine;
            if (valueElement.ValueKind == JsonValueKind.Null)
            {
                maxAlcoholicItemQuantityPerLine = null;
            }
            else if (valueElement.ValueKind == JsonValueKind.Number && valueElement.TryGetInt32(out var parsed))
            {
                // TryGetInt32 itself rejects fractional values (e.g. "5.5") and anything outside
                // the 32-bit range (e.g. > 2,147,483,647) - both AC4b and AC4d fall out of this
                // one check rather than needing separate range/shape validation.
                maxAlcoholicItemQuantityPerLine = parsed;
            }
            else
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = [$"{FieldName} must be an integer or null."] });
            }

            var command = new UpdateRestaurantSettingsCommand(maxAlcoholicItemQuantityPerLine);

            var errors = UpdateRestaurantSettingsValidator.Validate(command);
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = [.. errors] });
            }

            var result = await handler.HandleAsync(command, cancellationToken);

            return Results.Ok(new UpdateRestaurantSettingsResponse(result.MaxAlcoholicItemQuantityPerLine));
        }
    }

    private sealed record UpdateRestaurantSettingsResponse(int? MaxAlcoholicItemQuantityPerLine);
}
