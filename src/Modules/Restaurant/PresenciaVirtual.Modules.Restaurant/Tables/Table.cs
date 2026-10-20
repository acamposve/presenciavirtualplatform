namespace PresenciaVirtual.Modules.Restaurant.Tables;

/// <summary>
/// Aggregate root of the Tables capability. Only what CreateTable needs (BR1, BR2) is modeled
/// here; editing, deactivating, capacity, and floor sections are introduced by future
/// specifications (specs/restaurant/tables/create-table.md, Out of Scope).
/// </summary>
public sealed class Table
{
    public const int MaxLabelLength = 100;

    private Table(Guid id, Guid tenantId, string label, DateTimeOffset createdAt)
    {
        Id = id;
        TenantId = tenantId;
        Label = label;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }

    public Guid TenantId { get; }

    public string Label { get; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>Registers a new table (BR2: a non-empty label, at most <see cref="MaxLabelLength"/> characters).</summary>
    public static Table Register(Guid tenantId, string label, DateTimeOffset createdAt)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("Label is required.", nameof(label));
        }

        if (label.Length > MaxLabelLength)
        {
            throw new ArgumentException($"Label must not exceed {MaxLabelLength} characters.", nameof(label));
        }

        return new Table(Guid.NewGuid(), tenantId, label, createdAt);
    }

    /// <summary>
    /// Rehydrates an existing table from persistence (specs/restaurant/tables/list-tables.md).
    /// Not for creating new tables — use <see cref="Register"/>. Mirrors
    /// <see cref="Menu.MenuItem.Reconstruct(Guid, Guid, string, decimal, bool, DateTimeOffset)"/>.
    /// </summary>
    public static Table Reconstruct(Guid id, Guid tenantId, string label, DateTimeOffset createdAt)
        => new(id, tenantId, label, createdAt);
}
