namespace PresenciaVirtual.Modules.Restaurant.Tables;

/// <summary>
/// Table Management itself is out of scope of CreateOrder (specs/restaurant/ordering/create-order.md);
/// this is only the minimal read access CreateOrder needs to validate a table reference.
/// </summary>
public interface ITableRepository
{
    Task<bool> ExistsForTenantAsync(Guid tenantId, Guid tableId, CancellationToken cancellationToken = default);

    Task AddAsync(Table table, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one page of the tenant's tables (specs/restaurant/tables/list-tables.md), ordered by
    /// label (case-insensitive) then id, together with the tenant's total table count. Both reads
    /// MUST happen within a single transaction so the page and the count agree.
    /// </summary>
    Task<TablePage> GetPageAsync(Guid tenantId, int page, int pageSize, CancellationToken cancellationToken = default);
}
