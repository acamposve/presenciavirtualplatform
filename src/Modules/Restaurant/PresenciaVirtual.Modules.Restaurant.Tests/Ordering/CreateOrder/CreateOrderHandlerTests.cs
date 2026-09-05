using PresenciaVirtual.Modules.Core.Security;
using PresenciaVirtual.Modules.Restaurant.Ordering;
using PresenciaVirtual.Modules.Restaurant.Ordering.CreateOrder;
using PresenciaVirtual.Modules.Restaurant.Tables;
using Xunit;

namespace PresenciaVirtual.Modules.Restaurant.Tests.Ordering.CreateOrder;

public class CreateOrderHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-05T12:00:00Z");

    [Fact]
    public async Task HandleAsync_ThrowsTableNotFound_WhenTableDoesNotExistForTenant()
    {
        var tableId = Guid.NewGuid();
        var handler = CreateHandler(existingTables: []);

        await Assert.ThrowsAsync<TableNotFoundException>(
            () => handler.HandleAsync(new CreateOrderCommand(tableId, IdempotencyKey: null)));
    }

    [Fact]
    public async Task HandleAsync_ThrowsConflict_WhenTableAlreadyHasAnOpenOrder()
    {
        var tableId = Guid.NewGuid();
        var orderRepository = new FakeOrderRepository();
        orderRepository.Seed(Order.Open(TenantId, tableId, UserId, Now));
        var handler = CreateHandler(existingTables: [tableId], orderRepository: orderRepository);

        await Assert.ThrowsAsync<TableAlreadyHasOpenOrderException>(
            () => handler.HandleAsync(new CreateOrderCommand(tableId, IdempotencyKey: null)));
    }

    [Fact]
    public async Task HandleAsync_CreatesAnOpenOrder_ForAnUnoccupiedTable()
    {
        var tableId = Guid.NewGuid();
        var handler = CreateHandler(existingTables: [tableId]);

        var result = await handler.HandleAsync(new CreateOrderCommand(tableId, IdempotencyKey: null));

        Assert.False(result.IsReplay);
        Assert.Equal(tableId, result.TableId);
        Assert.Equal(OrderStatus.Open, result.Status);
        Assert.Equal(Now, result.CreatedAt);
    }

    [Fact]
    public async Task HandleAsync_ReplaysTheOriginalOrder_WhenTheIdempotencyKeyWasAlreadyUsedForTheSameTable()
    {
        var tableId = Guid.NewGuid();
        var handler = CreateHandler(existingTables: [tableId]);

        var first = await handler.HandleAsync(new CreateOrderCommand(tableId, IdempotencyKey: "key-1"));
        var replay = await handler.HandleAsync(new CreateOrderCommand(tableId, IdempotencyKey: "key-1"));

        Assert.False(first.IsReplay);
        Assert.True(replay.IsReplay);
        Assert.Equal(first.OrderId, replay.OrderId);
    }

    [Fact]
    public async Task HandleAsync_ThrowsConflict_WhenTheIdempotencyKeyIsReusedForADifferentTable()
    {
        var tableA = Guid.NewGuid();
        var tableB = Guid.NewGuid();
        var handler = CreateHandler(existingTables: [tableA, tableB]);

        await handler.HandleAsync(new CreateOrderCommand(tableA, IdempotencyKey: "key-1"));

        await Assert.ThrowsAsync<IdempotencyKeyConflictException>(
            () => handler.HandleAsync(new CreateOrderCommand(tableB, IdempotencyKey: "key-1")));
    }

    private static CreateOrderHandler CreateHandler(
        IEnumerable<Guid> existingTables,
        FakeOrderRepository? orderRepository = null)
    {
        // Mirrors OrderRepository.AddAsync persisting the order and its idempotency record
        // together: both fakes share one backing store instead of each keeping their own.
        var idempotencyRegistry = new Dictionary<(Guid TenantId, string Key), IdempotencyRecord>();

        return new(
            new FakeTableRepository(existingTables, TenantId),
            orderRepository ?? new FakeOrderRepository(idempotencyRegistry),
            new FakeIdempotencyStore(idempotencyRegistry),
            new FakeCurrentUserContext(TenantId, UserId),
            new FixedTimeProvider(Now));
    }

    private sealed class FakeTableRepository(IEnumerable<Guid> existingTableIds, Guid tenantId) : ITableRepository
    {
        private readonly HashSet<Guid> _existingTableIds = [.. existingTableIds];

        public Task<bool> ExistsForTenantAsync(Guid tenantIdArg, Guid tableId, CancellationToken cancellationToken = default)
            => Task.FromResult(tenantIdArg == tenantId && _existingTableIds.Contains(tableId));
    }

    private sealed class FakeOrderRepository(Dictionary<(Guid TenantId, string Key), IdempotencyRecord>? idempotencyRegistry = null) : IOrderRepository
    {
        private readonly List<Order> _orders = [];
        private readonly Dictionary<(Guid TenantId, string Key), IdempotencyRecord> _idempotencyRegistry = idempotencyRegistry ?? [];

        public void Seed(Order order) => _orders.Add(order);

        public Task<bool> HasOpenOrderAsync(Guid tenantId, Guid tableId, CancellationToken cancellationToken = default)
            => Task.FromResult(_orders.Any(o => o.TenantId == tenantId && o.TableId == tableId && o.Status == OrderStatus.Open));

        public Task AddAsync(Order order, string? idempotencyKey, CancellationToken cancellationToken = default)
        {
            if (_orders.Any(o => o.TenantId == order.TenantId && o.TableId == order.TableId && o.Status == OrderStatus.Open))
            {
                throw new TableAlreadyHasOpenOrderException(order.TableId);
            }

            if (idempotencyKey is { Length: > 0 } && _idempotencyRegistry.ContainsKey((order.TenantId, idempotencyKey)))
            {
                throw new IdempotencyKeyRaceLostException(idempotencyKey);
            }

            _orders.Add(order);

            if (idempotencyKey is { Length: > 0 })
            {
                _idempotencyRegistry[(order.TenantId, idempotencyKey)] = new IdempotencyRecord(order.Id, order.TableId);
            }

            return Task.CompletedTask;
        }

        public Task<Order?> GetAsync(Guid tenantId, Guid orderId, CancellationToken cancellationToken = default)
            => Task.FromResult(_orders.SingleOrDefault(o => o.TenantId == tenantId && o.Id == orderId));
    }

    private sealed class FakeIdempotencyStore(Dictionary<(Guid TenantId, string Key), IdempotencyRecord> records) : IIdempotencyStore
    {
        public Task<IdempotencyRecord?> FindAsync(Guid tenantId, string idempotencyKey, CancellationToken cancellationToken = default)
            => Task.FromResult(records.GetValueOrDefault((tenantId, idempotencyKey)));
    }

    private sealed class FakeCurrentUserContext(Guid tenantId, Guid userId) : ICurrentUserContext
    {
        public Guid TenantId { get; } = tenantId;
        public Guid UserId { get; } = userId;
        public bool HasPermission(string permission) => true;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
