namespace PresenciaVirtual.Modules.Restaurant.Settings;

/// <summary>
/// Aggregate for per-tenant Ordering configuration (specs/restaurant/settings/update-restaurant-settings.md).
/// Currently exposes only MaxAlcoholicItemQuantityPerLine (specs/restaurant/ordering/add-item.md BR7).
/// </summary>
public sealed class RestaurantSettings
{
    private RestaurantSettings(Guid tenantId, int? maxAlcoholicItemQuantityPerLine)
    {
        TenantId = tenantId;
        MaxAlcoholicItemQuantityPerLine = maxAlcoholicItemQuantityPerLine;
    }

    public Guid TenantId { get; }

    /// <summary>Null means no limit is configured (specs/restaurant/ordering/add-item.md BR7).</summary>
    public int? MaxAlcoholicItemQuantityPerLine { get; }

    /// <summary>Configures a tenant's settings (BR2: MaxAlcoholicItemQuantityPerLine, when supplied, must be a positive integer — null explicitly clears any existing limit).</summary>
    public static RestaurantSettings Configure(Guid tenantId, int? maxAlcoholicItemQuantityPerLine)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        if (maxAlcoholicItemQuantityPerLine is <= 0)
        {
            throw new ArgumentException("MaxAlcoholicItemQuantityPerLine must be a positive value when supplied.", nameof(maxAlcoholicItemQuantityPerLine));
        }

        return new RestaurantSettings(tenantId, maxAlcoholicItemQuantityPerLine);
    }
}
