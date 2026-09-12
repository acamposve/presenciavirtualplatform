namespace PresenciaVirtual.Modules.Restaurant.Tables;

/// <summary>
/// Table Management itself is out of scope of CreateOrder (specs/restaurant/ordering/create-order.md);
/// this is only the minimal read access CreateOrder needs to validate a table reference.
/// </summary>
public interface ITableRepository
{
    Task<bool> ExistsForTenantAsync(Guid tenantId, Guid tableId, CancellationToken cancellationToken = default);
}
