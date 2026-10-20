namespace PresenciaVirtual.Modules.Restaurant.Tables.ListTables;

/// <summary>
/// FR2: the full representation of a table for a listing. Deliberately no <c>TenantId</c>,
/// matching <c>CreateTable</c>'s response shape.
/// </summary>
public sealed record TableSummary(Guid TableId, string Label, DateTimeOffset CreatedAt);

/// <summary>
/// FR3: one page of the tenant's tables, the page and page size actually applied (so defaulted
/// values are visible to the caller), and the tenant's total table count.
/// </summary>
public sealed record ListTablesResult(IReadOnlyList<TableSummary> Items, int Page, int PageSize, int TotalCount)
{
    public static ListTablesResult From(TablePage page, int requestedPage, int requestedPageSize)
        => new(
            [.. page.Items.Select(t => new TableSummary(t.Id, t.Label, t.CreatedAt))],
            requestedPage,
            requestedPageSize,
            page.TotalCount);
}
