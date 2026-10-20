using PresenciaVirtual.Modules.Restaurant.Paging;

namespace PresenciaVirtual.Modules.Restaurant.Tables.ListTables;

public static class ListTablesValidator
{
    /// <summary>Returns validation error messages; empty when the query is valid (FR4/FR5, shared paging bounds).</summary>
    public static IReadOnlyList<string> Validate(ListTablesQuery query)
        => PageRequest.Validate(query.Page, query.PageSize);
}
