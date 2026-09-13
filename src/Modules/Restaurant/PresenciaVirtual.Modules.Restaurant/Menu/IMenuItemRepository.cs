namespace PresenciaVirtual.Modules.Restaurant.Menu;

public interface IMenuItemRepository
{
    Task<MenuItem?> GetAsync(Guid tenantId, Guid menuItemId, CancellationToken cancellationToken = default);

    Task AddAsync(MenuItem menuItem, CancellationToken cancellationToken = default);
}
