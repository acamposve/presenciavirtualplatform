using PresenciaVirtual.Modules.Core.Security;

namespace PresenciaVirtual.Modules.Restaurant.Tables.CreateTable;

public sealed class CreateTableHandler(ITableRepository tableRepository, ICurrentUserContext currentUser, TimeProvider timeProvider)
{
    public async Task<CreateTableResult> HandleAsync(CreateTableCommand command, CancellationToken cancellationToken = default)
    {
        var table = Table.Register(currentUser.TenantId, command.Label, timeProvider.GetUtcNow());

        await tableRepository.AddAsync(table, cancellationToken);

        return new CreateTableResult(table.Id, table.Label);
    }
}
