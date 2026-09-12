namespace PresenciaVirtual.Modules.Restaurant.Settings;

/// <summary>
/// Minimal reference concept, not a full Restaurant configuration aggregate
/// (specs/restaurant/ordering/add-item.md, BR7): there is no capability to configure these
/// settings yet, only to read them.
/// </summary>
public interface IRestaurantSettingsRepository
{
    /// <summary>Null means no limit is configured for the tenant (a missing row is not an error).</summary>
    Task<int?> GetMaxAlcoholicItemQuantityPerLineAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
