using PresenciaVirtual.Modules.Core.Security;

namespace PresenciaVirtual.Modules.Restaurant.Tables.ListTables;

public sealed class ListTablesHandler(ITableRepository tableRepository, ICurrentUserContext currentUser)
{
    public async Task<ListTablesResult> HandleAsync(ListTablesQuery query, CancellationToken cancellationToken = default)
    {
        // BR1: the listing is always scoped to the caller's tenant, never to client input (ADR 0002
        // rules 2-3). There is no "all tenants" form of this read.
        var page = await tableRepository.GetPageAsync(currentUser.TenantId, query.Page, query.PageSize, cancellationToken);

        return ListTablesResult.From(page, query.Page, query.PageSize);
    }
}
