namespace PresenciaVirtual.Modules.Restaurant.Settings;

public interface IRestaurantSettingsRepository
{
    /// <summary>Null means no limit is configured for the tenant (a missing row is not an error).</summary>
    Task<int?> GetMaxAlcoholicItemQuantityPerLineAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Creates the tenant's settings row if none exists, or updates it if one already does (specs/restaurant/settings/update-restaurant-settings.md BR3).</summary>
    Task UpsertAsync(RestaurantSettings settings, CancellationToken cancellationToken = default);
}
