namespace PresenciaVirtual.Modules.Restaurant.Menu;

/// <summary>
/// One page of a tenant's menu items plus the total number of items that tenant has
/// (specs/restaurant/menu/get-menu-items.md FR3/BR5). Both values are read in one transaction, so
/// the page and the count always agree with each other.
/// </summary>
public sealed record MenuItemPage(IReadOnlyList<MenuItem> Items, int TotalCount);
