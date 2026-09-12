using PresenciaVirtual.Modules.Core.Security;
using PresenciaVirtual.Modules.Restaurant.Menu;
using PresenciaVirtual.Modules.Restaurant.Settings;

namespace PresenciaVirtual.Modules.Restaurant.Ordering.AddItem;

public sealed class AddItemHandler(
    IOrderRepository orderRepository,
    IMenuItemRepository menuItemRepository,
    IOrderItemRepository orderItemRepository,
    IRestaurantSettingsRepository settingsRepository,
    ICurrentUserContext currentUser)
{
    public async Task<AddItemResult> HandleAsync(AddItemCommand command, CancellationToken cancellationToken = default)
    {
        var tenantId = currentUser.TenantId;

        var order = await orderRepository.GetAsync(tenantId, command.OrderId, cancellationToken)
            ?? throw new OrderNotFoundException(command.OrderId);

        if (order.Status != OrderStatus.Open)
        {
            throw new OrderNotOpenException(command.OrderId);
        }

        var menuItem = await menuItemRepository.GetAsync(tenantId, command.MenuItemId, cancellationToken)
            ?? throw new MenuItemNotFoundException(command.MenuItemId);

        int? maxAlcoholicQuantity = menuItem.IsAlcoholic
            ? await settingsRepository.GetMaxAlcoholicItemQuantityPerLineAsync(tenantId, cancellationToken)
            : null;

        var mergeRequest = new AddItemMergeRequest(
            tenantId,
            command.OrderId,
            command.MenuItemId,
            command.Quantity,
            menuItem.Price,
            menuItem.IsAlcoholic,
            maxAlcoholicQuantity,
            command.IdempotencyKey);

        // BR6: whether this call applies the mutation or replays a prior one, the response
        // below always re-reads the order's current state — never a frozen snapshot.
        await orderItemRepository.AddOrMergeAsync(mergeRequest, cancellationToken);

        var items = await orderItemRepository.GetByOrderAsync(tenantId, command.OrderId, cancellationToken);
        return AddItemResult.From(command.OrderId, items);
    }
}
