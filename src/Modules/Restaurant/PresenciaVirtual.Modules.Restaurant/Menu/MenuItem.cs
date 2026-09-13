namespace PresenciaVirtual.Modules.Restaurant.Menu;

/// <summary>
/// Aggregate root of the Menu capability (specs/restaurant/menu/create-menu-item.md). Editing,
/// deactivating, and categorization are introduced by future specifications.
/// </summary>
public sealed class MenuItem
{
    public const int MaxNameLength = 200;

    /// <summary>The largest value restaurant.menu_items.price's numeric(10, 2) column can represent.</summary>
    public const decimal MaxPrice = 99_999_999.99m;

    private MenuItem(Guid id, Guid tenantId, string name, decimal price, bool isAlcoholic, DateTimeOffset createdAt)
    {
        Id = id;
        TenantId = tenantId;
        Name = name;
        Price = price;
        IsAlcoholic = isAlcoholic;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }

    public Guid TenantId { get; }

    public string Name { get; }

    public decimal Price { get; }

    public bool IsAlcoholic { get; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>Registers a new menu item (BR2: a non-empty name, at most <see cref="MaxNameLength"/> characters; BR3: a positive price, at most <see cref="MaxPrice"/>, with at most 2 decimal places; BR4: IsAlcoholic defaults to false when not supplied by the caller).</summary>
    public static MenuItem Register(Guid tenantId, string name, decimal price, bool isAlcoholic, DateTimeOffset createdAt)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        if (name.Length > MaxNameLength)
        {
            throw new ArgumentException($"Name must not exceed {MaxNameLength} characters.", nameof(name));
        }

        if (price <= 0)
        {
            throw new ArgumentException("Price must be a positive value.", nameof(price));
        }

        if (price > MaxPrice)
        {
            throw new ArgumentException($"Price must not exceed {MaxPrice}.", nameof(price));
        }

        if (decimal.Round(price, 2) != price)
        {
            throw new ArgumentException("Price must not have more than 2 decimal places.", nameof(price));
        }

        return new MenuItem(Guid.NewGuid(), tenantId, name, price, isAlcoholic, createdAt);
    }

    /// <summary>Rehydrates an existing menu item from persistence. Not for creating new menu items — use <see cref="Register"/>.</summary>
    public static MenuItem Reconstruct(Guid id, Guid tenantId, string name, decimal price, bool isAlcoholic, DateTimeOffset createdAt)
        => new(id, tenantId, name, price, isAlcoholic, createdAt);
}
