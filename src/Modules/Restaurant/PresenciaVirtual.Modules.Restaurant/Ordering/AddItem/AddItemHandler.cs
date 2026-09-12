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

        if (command.IdempotencyKey is { Length: > 0 } idempotencyKey)
        {
            // Checked before resolving the order/menu item below: a key reused against a
            // different (possibly nonexistent) OrderId/MenuItemId must be a 409 conflict per
            // AC9, not a 404 that leaks past the identity check first.
            var existingClaim = await orderItemRepository.FindIdempotencyClaimAsync(tenantId, idempotencyKey, cancellationToken);
            if (existingClaim is not null)
            {
                return await ReplayOrConflictAsync(tenantId, idempotencyKey, existingClaim, command, cancellationToken);
            }
        }

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

        // Covers the race between the upfront check above and now: if a concurrent request
        // claims the same key in between, AddOrMergeAsync itself re-checks atomically and
        // throws IdempotencyKeyRaceLostException rather than double-applying.
        await orderItemRepository.AddOrMergeAsync(mergeRequest, cancellationToken);

        return await CurrentStateAsync(tenantId, command.OrderId, cancellationToken);
    }

    private async Task<AddItemResult> ReplayOrConflictAsync(Guid tenantId, string idempotencyKey, AddItemIdempotencyClaim existingClaim, AddItemCommand command, CancellationToken cancellationToken)
    {
        var matches = existingClaim.OrderId == command.OrderId
            && existingClaim.MenuItemId == command.MenuItemId
            && existingClaim.Quantity == command.Quantity;

        if (!matches)
        {
            throw new AddItemIdempotencyKeyConflictException(idempotencyKey);
        }

        // A matched replay never re-applies the mutation (BR6) - and, since the original call
        // already proved the order/menu item existed, there is no need to re-validate them here.
        return await CurrentStateAsync(tenantId, existingClaim.OrderId, cancellationToken);
    }

    private async Task<AddItemResult> CurrentStateAsync(Guid tenantId, Guid orderId, CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetAsync(tenantId, orderId, cancellationToken)
            ?? throw new InvalidOperationException($"Order '{orderId}' was expected to still exist but was not found.");

        return AddItemResult.From(order);
    }
}
