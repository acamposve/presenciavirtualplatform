using PresenciaVirtual.Modules.Core.Security;
using PresenciaVirtual.Modules.Restaurant.Ordering;
using PresenciaVirtual.Modules.Restaurant.Ordering.RemoveItem;
using Xunit;

namespace PresenciaVirtual.Modules.Restaurant.Tests.Ordering.RemoveItem;

public class RemoveItemHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task HandleAsync_PassesTheCallersTenantAndTheCommandsIdentifiersToTheRepository()
    {
        var order = OrderWithItem(TenantId);
        var repository = new FakeOrderRepository(order);
        var handler = new RemoveItemHandler(repository, new FakeCurrentUserContext(TenantId));
        var orderId = Guid.NewGuid();
        var menuItemId = Guid.NewGuid();

        await handler.HandleAsync(new RemoveItemCommand(orderId, menuItemId, "key-1"));

        Assert.Equal((TenantId, orderId, menuItemId, "key-1"), repository.LastCall);
    }

    [Fact]
    public async Task HandleAsync_ReturnsTheOrdersRemainingItemsAndTotal()
    {
        // The repository returns the order *after* the line was removed; the handler must project
        // exactly what it was given rather than recomputing or re-adding anything.
        var remaining = new OrderItem(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2, 4.5m);
        var order = Order.Reconstruct(Guid.NewGuid(), TenantId, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, OrderStatus.Open, [remaining]);
        var handler = new RemoveItemHandler(new FakeOrderRepository(order), new FakeCurrentUserContext(TenantId));

        var result = await handler.HandleAsync(new RemoveItemCommand(order.Id, Guid.NewGuid(), null));

        var line = Assert.Single(result.Items);
        Assert.Equal(remaining.MenuItemId, line.MenuItemId);
        Assert.Equal(2, line.Quantity);
        Assert.Equal(9m, result.Total);
    }

    [Fact]
    public async Task HandleAsync_PropagatesTheRepositoriesNotFoundAndConflictExceptionsUnchanged()
    {
        var handler = new RemoveItemHandler(new FakeOrderRepository(failure: new RemoveItemOrderNotFoundException(Guid.NewGuid())), new FakeCurrentUserContext(TenantId));

        await Assert.ThrowsAsync<RemoveItemOrderNotFoundException>(
            () => handler.HandleAsync(new RemoveItemCommand(Guid.NewGuid(), Guid.NewGuid(), null)));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("key-1", true)]
    public void Validate_RejectsAnEmptyIdempotencyKey(string key, bool expectedValid)
    {
        var errors = RemoveItemValidator.Validate(new RemoveItemCommand(Guid.NewGuid(), Guid.NewGuid(), key));

        Assert.Equal(expectedValid, errors.Count == 0);
    }

    [Fact]
    public void Validate_AcceptsAnIdempotencyKeyAtTheMaximumLength()
    {
        var errors = RemoveItemValidator.Validate(new RemoveItemCommand(Guid.NewGuid(), Guid.NewGuid(), new string('k', 200)));

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RejectsAnIdempotencyKeyLongerThanTheMaximum()
    {
        var errors = RemoveItemValidator.Validate(new RemoveItemCommand(Guid.NewGuid(), Guid.NewGuid(), new string('k', 201)));

        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Validate_RejectsAnEmptyMenuItemId()
    {
        // Defence in depth only: a malformed/missing route segment 404s before the handler runs.
        var errors = RemoveItemValidator.Validate(new RemoveItemCommand(Guid.NewGuid(), Guid.Empty, null));

        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Validate_AcceptsAValidCommand()
    {
        var errors = RemoveItemValidator.Validate(new RemoveItemCommand(Guid.NewGuid(), Guid.NewGuid(), null));

        Assert.Empty(errors);
    }

    private static Order OrderWithItem(Guid tenantId)
    {
        var item = new OrderItem(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, 3m);
        return Order.Reconstruct(Guid.NewGuid(), tenantId, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, OrderStatus.Open, [item]);
    }

    private sealed class FakeOrderRepository(Order? result = null, Exception? failure = null) : IOrderRepository
    {
        public (Guid TenantId, Guid OrderId, Guid MenuItemId, string? Key)? LastCall { get; private set; }

        public Task<bool> HasOpenOrderAsync(Guid tenantId, Guid tableId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by RemoveItemHandler.");

        public Task AddAsync(Order order, string? idempotencyKey, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by RemoveItemHandler.");

        public Task<Order?> GetAsync(Guid tenantId, Guid orderId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by RemoveItemHandler.");

        public Task<Order?> GetOpenByTableAsync(Guid tenantId, Guid tableId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by RemoveItemHandler.");

        public Task<Order> CloseAsync(Guid tenantId, Guid orderId, string? idempotencyKey, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by RemoveItemHandler.");

        public Task<Order> CancelAsync(Guid tenantId, Guid orderId, string? idempotencyKey, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by RemoveItemHandler.");

        public Task<Order> RemoveItemAsync(Guid tenantId, Guid orderId, Guid menuItemId, string? idempotencyKey, CancellationToken cancellationToken = default)
        {
            LastCall = (tenantId, orderId, menuItemId, idempotencyKey);

            // Allows a test to model the repository rejecting the request instead of returning.
            return failure is not null
                ? Task.FromException<Order>(failure)
                : Task.FromResult(result!);
        }
    }

    private sealed class FakeCurrentUserContext(Guid tenantId) : ICurrentUserContext
    {
        public Guid TenantId { get; } = tenantId;
        public Guid UserId { get; } = Guid.NewGuid();
        public bool HasPermission(string permission) => true;
    }
}
