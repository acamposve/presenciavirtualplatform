using PresenciaVirtual.Modules.Core.Security;

namespace PresenciaVirtual.Modules.Restaurant.Menu.GetMenuItems;

public sealed class GetMenuItemsHandler(IMenuItemRepository menuItemRepository, ICurrentUserContext currentUser)
{
    public async Task<GetMenuItemsResult> HandleAsync(GetMenuItemsQuery query, CancellationToken cancellationToken = default)
    {
        // BR1: the listing is always scoped to the caller's tenant, never to client input (ADR 0002
        // rules 2-3). There is no "all tenants" form of this read.
        var page = await menuItemRepository.GetPageAsync(currentUser.TenantId, query.Page, query.PageSize, cancellationToken);

        return GetMenuItemsResult.From(page, query.Page, query.PageSize);
    }
}
