namespace PresenciaVirtual.Modules.Restaurant.Menu;

public interface IMenuItemRepository
{
    Task<MenuItem?> GetAsync(Guid tenantId, Guid menuItemId, CancellationToken cancellationToken = default);

    Task AddAsync(MenuItem menuItem, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one page of the tenant's menu items (specs/restaurant/menu/get-menu-items.md),
    /// ordered by name (case-insensitive) then id, together with the tenant's total item count.
    /// Both reads MUST happen within a single transaction so the page and the count agree.
    /// </summary>
    Task<MenuItemPage> GetPageAsync(Guid tenantId, int page, int pageSize, CancellationToken cancellationToken = default);
}
