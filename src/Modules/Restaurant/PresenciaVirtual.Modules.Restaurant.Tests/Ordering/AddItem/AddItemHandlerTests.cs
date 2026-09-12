using PresenciaVirtual.Modules.Core.Security;
using PresenciaVirtual.Modules.Restaurant.Menu;
using PresenciaVirtual.Modules.Restaurant.Ordering;
using PresenciaVirtual.Modules.Restaurant.Ordering.AddItem;
using PresenciaVirtual.Modules.Restaurant.Settings;
using Xunit;

namespace PresenciaVirtual.Modules.Restaurant.Tests.Ordering.AddItem;

public class AddItemHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    // Note: Order's status is always Open by construction today (see Order.cs) - there is no
    // way to build a non-Open order to unit-test the BR1/OrderNotOpenException branch until a
    // future specification (CloseOrder/CancelOrder) introduces another status, exactly as
    // add-item.md's AC3 already states. That branch is intentionally not covered here.

    [Fact]
    public async Task HandleAsync_ThrowsOrderNotFound_WhenOrderDoesNotExist()
    {
        var handler = CreateHandler(orders: [], menuItems: []);

        await Assert.ThrowsAsync<OrderNotFoundException>(
            () => handler.HandleAsync(new AddItemCommand(Guid.NewGuid(), Guid.NewGuid(), 1, null)));
    }

    [Fact]
    public async Task HandleAsync_ThrowsMenuItemNotFound_WhenMenuItemDoesNotExist()
    {
        var order = Order.Open(TenantId, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);
        var handler = CreateHandler(orders: [order], menuItems: []);

        await Assert.ThrowsAsync<MenuItemNotFoundException>(
            () => handler.HandleAsync(new AddItemCommand(order.Id, Guid.NewGuid(), 1, null)));
    }

    [Fact]
    public async Task HandleAsync_DoesNotQuerySettings_WhenMenuItemIsNotAlcoholic()
    {
        var order = Order.Open(TenantId, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);
        var menuItem = new MenuItem(Guid.NewGuid(), TenantId, "Coke", 3m, IsAlcoholic: false);
        var settings = new FakeRestaurantSettingsRepository(5);
        var handler = CreateHandler(orders: [order], menuItems: [menuItem], settings: settings);

        await handler.HandleAsync(new AddItemCommand(order.Id, menuItem.Id, 1, null));

        Assert.Equal(0, settings.CallCount);
    }

    [Fact]
    public async Task HandleAsync_QueriesSettings_WhenMenuItemIsAlcoholic()
    {
        var order = Order.Open(TenantId, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);
        var menuItem = new MenuItem(Guid.NewGuid(), TenantId, "Beer", 5m, IsAlcoholic: true);
        var settings = new FakeRestaurantSettingsRepository(5);
        var handler = CreateHandler(orders: [order], menuItems: [menuItem], settings: settings);

        await handler.HandleAsync(new AddItemCommand(order.Id, menuItem.Id, 1, null));

        Assert.Equal(1, settings.CallCount);
    }

    [Fact]
    public async Task HandleAsync_ReturnsCurrentOrderStateAfterTheMerge()
    {
        var order = Order.Open(TenantId, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);
        var menuItem = new MenuItem(Guid.NewGuid(), TenantId, "Coke", 3m, IsAlcoholic: false);
        var orderItemRepository = new FakeOrderItemRepository();
        var handler = CreateHandler(orders: [order], menuItems: [menuItem], orderItemRepository: orderItemRepository);

        var result = await handler.HandleAsync(new AddItemCommand(order.Id, menuItem.Id, 2, null));

        var line = Assert.Single(result.Items);
        Assert.Equal(menuItem.Id, line.MenuItemId);
        Assert.Equal(2, line.Quantity);
        Assert.Equal(3m, line.UnitPriceSnapshot);
        Assert.Equal(6m, line.LineTotal);
        Assert.Equal(6m, result.Total);
    }

    private static AddItemHandler CreateHandler(
        IEnumerable<Order> orders,
        IEnumerable<MenuItem> menuItems,
        FakeOrderItemRepository? orderItemRepository = null,
        FakeRestaurantSettingsRepository? settings = null)
        => new(
            new FakeOrderRepository(orders),
            new FakeMenuItemRepository(menuItems),
            orderItemRepository ?? new FakeOrderItemRepository(),
            settings ?? new FakeRestaurantSettingsRepository(null),
            new FakeCurrentUserContext(TenantId));

    private sealed class FakeOrderRepository(IEnumerable<Order> orders) : IOrderRepository
    {
        private readonly List<Order> _orders = [.. orders];

        public Task<bool> HasOpenOrderAsync(Guid tenantId, Guid tableId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by AddItemHandler.");

        public Task AddAsync(Order order, string? idempotencyKey, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by AddItemHandler.");

        public Task<Order?> GetAsync(Guid tenantId, Guid orderId, CancellationToken cancellationToken = default)
            => Task.FromResult(_orders.SingleOrDefault(o => o.TenantId == tenantId && o.Id == orderId));
    }

    private sealed class FakeMenuItemRepository(IEnumerable<MenuItem> menuItems) : IMenuItemRepository
    {
        private readonly List<MenuItem> _menuItems = [.. menuItems];

        public Task<MenuItem?> GetAsync(Guid tenantId, Guid menuItemId, CancellationToken cancellationToken = default)
            => Task.FromResult(_menuItems.SingleOrDefault(m => m.TenantId == tenantId && m.Id == menuItemId));
    }

    private sealed class FakeOrderItemRepository : IOrderItemRepository
    {
        private readonly List<OrderItem> _items = [];

        public Task<IReadOnlyList<OrderItem>> GetByOrderAsync(Guid tenantId, Guid orderId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<OrderItem>>(_items.Where(i => i.OrderId == orderId).ToList());

        public Task<AddItemOutcome> AddOrMergeAsync(AddItemMergeRequest request, CancellationToken cancellationToken = default)
        {
            var existing = _items.FirstOrDefault(i => i.OrderId == request.OrderId && i.MenuItemId == request.MenuItemId);
            if (existing is not null)
            {
                _items.Remove(existing);
                _items.Add(existing with { Quantity = existing.Quantity + request.Quantity });
            }
            else
            {
                _items.Add(new OrderItem(Guid.NewGuid(), request.OrderId, request.MenuItemId, request.Quantity, request.UnitPriceSnapshot));
            }

            return Task.FromResult(AddItemOutcome.Applied);
        }
    }

    private sealed class FakeRestaurantSettingsRepository(int? limit) : IRestaurantSettingsRepository
    {
        public int CallCount { get; private set; }

        public Task<int?> GetMaxAlcoholicItemQuantityPerLineAsync(Guid tenantId, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(limit);
        }
    }

    private sealed class FakeCurrentUserContext(Guid tenantId) : ICurrentUserContext
    {
        public Guid TenantId { get; } = tenantId;
        public Guid UserId { get; } = Guid.NewGuid();
        public bool HasPermission(string permission) => true;
    }
}
