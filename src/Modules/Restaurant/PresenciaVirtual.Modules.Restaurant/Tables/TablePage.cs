namespace PresenciaVirtual.Modules.Restaurant.Tables;

/// <summary>
/// One page of a tenant's tables plus the total number of tables that tenant has
/// (specs/restaurant/tables/list-tables.md FR3/BR5). Both values are read in one transaction, so
/// the page and the count always agree with each other.
/// </summary>
public sealed record TablePage(IReadOnlyList<Table> Items, int TotalCount);
