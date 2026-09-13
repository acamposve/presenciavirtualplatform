using PresenciaVirtual.Modules.Core.Security;

namespace PresenciaVirtual.Modules.Restaurant.Menu.CreateMenuItem;

public sealed class CreateMenuItemHandler(IMenuItemRepository menuItemRepository, ICurrentUserContext currentUser, TimeProvider timeProvider)
{
    public async Task<CreateMenuItemResult> HandleAsync(CreateMenuItemCommand command, CancellationToken cancellationToken = default)
    {
        var menuItem = MenuItem.Register(currentUser.TenantId, command.Name, command.Price, command.IsAlcoholic, timeProvider.GetUtcNow());

        await menuItemRepository.AddAsync(menuItem, cancellationToken);

        return new CreateMenuItemResult(menuItem.Id, menuItem.Name, menuItem.Price, menuItem.IsAlcoholic);
    }
}
