using PresenciaVirtual.Modules.Core.Security;
using PresenciaVirtual.Modules.Restaurant.Tables;
using PresenciaVirtual.Modules.Restaurant.Tables.CreateTable;
using Xunit;

namespace PresenciaVirtual.Modules.Restaurant.Tests.Tables.CreateTable;

public class CreateTableHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-12T12:00:00Z");

    [Fact]
    public async Task HandleAsync_RegistersATableForTheCurrentTenant()
    {
        var repository = new FakeTableRepository();
        var handler = new CreateTableHandler(repository, new FakeCurrentUserContext(TenantId), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateTableCommand("Table 5"));

        Assert.Equal("Table 5", result.Label);
        Assert.NotEqual(Guid.Empty, result.TableId);
        var saved = Assert.Single(repository.SavedTables);
        Assert.Equal(TenantId, saved.TenantId);
        Assert.Equal(result.TableId, saved.Id);
    }

    private sealed class FakeTableRepository : ITableRepository
    {
        public List<Table> SavedTables { get; } = [];

        public Task<bool> ExistsForTenantAsync(Guid tenantId, Guid tableId, CancellationToken cancellationToken = default)
            => Task.FromResult(SavedTables.Any(t => t.TenantId == tenantId && t.Id == tableId));

        public Task AddAsync(Table table, CancellationToken cancellationToken = default)
        {
            SavedTables.Add(table);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCurrentUserContext(Guid tenantId) : ICurrentUserContext
    {
        public Guid TenantId { get; } = tenantId;
        public Guid UserId { get; } = Guid.NewGuid();
        public bool HasPermission(string permission) => true;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
