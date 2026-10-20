using PresenciaVirtual.Modules.Restaurant.Paging;

namespace PresenciaVirtual.Modules.Restaurant.Menu.GetMenuItems;

public static class GetMenuItemsValidator
{
    /// <summary>Returns validation error messages; empty when the query is valid (FR4/FR5, shared paging bounds).</summary>
    public static IReadOnlyList<string> Validate(GetMenuItemsQuery query)
        => PageRequest.Validate(query.Page, query.PageSize);
}
