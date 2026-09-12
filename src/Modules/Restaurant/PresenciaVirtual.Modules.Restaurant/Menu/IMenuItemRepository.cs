namespace PresenciaVirtual.Modules.Restaurant.Menu;

public interface IMenuItemRepository
{
    Task<MenuItem?> GetAsync(Guid tenantId, Guid menuItemId, CancellationToken cancellationToken = default);
}
