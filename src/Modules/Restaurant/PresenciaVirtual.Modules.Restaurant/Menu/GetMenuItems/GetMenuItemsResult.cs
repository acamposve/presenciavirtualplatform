namespace PresenciaVirtual.Modules.Restaurant.Menu.GetMenuItems;

/// <summary>
/// FR2: the full representation of a menu item for a listing — the same fields
/// <c>CreateMenuItem</c> writes. Deliberately no <c>TenantId</c>, matching that capability's
/// response shape.
/// </summary>
public sealed record MenuItemSummary(Guid MenuItemId, string Name, decimal Price, bool IsAlcoholic, DateTimeOffset CreatedAt);

/// <summary>
/// FR3: one page of the tenant's menu items, the page and page size actually applied (so defaulted
/// values are visible to the caller), and the tenant's total item count.
/// </summary>
public sealed record GetMenuItemsResult(IReadOnlyList<MenuItemSummary> Items, int Page, int PageSize, int TotalCount)
{
    public static GetMenuItemsResult From(MenuItemPage page, int requestedPage, int requestedPageSize)
        => new(
            [.. page.Items.Select(i => new MenuItemSummary(i.Id, i.Name, i.Price, i.IsAlcoholic, i.CreatedAt))],
            requestedPage,
            requestedPageSize,
            page.TotalCount);
}
