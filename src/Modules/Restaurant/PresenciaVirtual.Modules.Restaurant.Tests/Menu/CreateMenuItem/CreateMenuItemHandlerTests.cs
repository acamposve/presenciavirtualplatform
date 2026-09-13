using PresenciaVirtual.Modules.Core.Security;
using PresenciaVirtual.Modules.Restaurant.Menu;
using PresenciaVirtual.Modules.Restaurant.Menu.CreateMenuItem;
using Xunit;

namespace PresenciaVirtual.Modules.Restaurant.Tests.Menu.CreateMenuItem;

public class CreateMenuItemHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-13T12:00:00Z");

    [Fact]
    public async Task HandleAsync_RegistersAMenuItemForTheCurrentTenant()
    {
        var repository = new FakeMenuItemRepository();
        var handler = new CreateMenuItemHandler(repository, new FakeCurrentUserContext(TenantId), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateMenuItemCommand("Coke", 3m, IsAlcoholic: false));

        Assert.Equal("Coke", result.Name);
        Assert.Equal(3m, result.Price);
        Assert.False(result.IsAlcoholic);
        Assert.NotEqual(Guid.Empty, result.MenuItemId);
        var saved = Assert.Single(repository.SavedMenuItems);
        Assert.Equal(TenantId, saved.TenantId);
        Assert.Equal(result.MenuItemId, saved.Id);
    }

    [Fact]
    public async Task HandleAsync_PropagatesIsAlcoholic()
    {
        var repository = new FakeMenuItemRepository();
        var handler = new CreateMenuItemHandler(repository, new FakeCurrentUserContext(TenantId), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateMenuItemCommand("Beer", 5m, IsAlcoholic: true));

        Assert.True(result.IsAlcoholic);
    }

    private sealed class FakeMenuItemRepository : IMenuItemRepository
    {
        public List<MenuItem> SavedMenuItems { get; } = [];

        public Task<MenuItem?> GetAsync(Guid tenantId, Guid menuItemId, CancellationToken cancellationToken = default)
            => Task.FromResult(SavedMenuItems.SingleOrDefault(m => m.TenantId == tenantId && m.Id == menuItemId));

        public Task AddAsync(MenuItem menuItem, CancellationToken cancellationToken = default)
        {
            SavedMenuItems.Add(menuItem);
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
