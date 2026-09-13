using PresenciaVirtual.Modules.Restaurant.Ordering;
using Xunit;

namespace PresenciaVirtual.Modules.Restaurant.Tests.Ordering;

public class OrderTests
{
    [Fact]
    public void Open_StartsInOpenStatus()
    {
        var order = Order.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(OrderStatus.Open, order.Status);
    }

    [Fact]
    public void Open_StartsWithZeroTotal()
    {
        var order = Order.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(0m, order.Total);
    }

    [Fact]
    public void Open_AssignsATenantScopedIdentity()
    {
        var tenantId = Guid.NewGuid();
        var tableId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var order = Order.Open(tenantId, tableId, userId, now);

        Assert.NotEqual(Guid.Empty, order.Id);
        Assert.Equal(tenantId, order.TenantId);
        Assert.Equal(tableId, order.TableId);
        Assert.Equal(userId, order.CreatedByUserId);
        Assert.Equal(now, order.CreatedAt);
    }

    [Fact]
    public void Open_GeneratesADifferentIdEachTime()
    {
        var tenantId = Guid.NewGuid();
        var tableId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var first = Order.Open(tenantId, tableId, userId, now);
        var second = Order.Open(tenantId, tableId, userId, now);

        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void Open_RejectsAnEmptyTenantId()
    {
        Assert.Throws<ArgumentException>(() => Order.Open(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Open_RejectsAnEmptyTableId()
    {
        Assert.Throws<ArgumentException>(() => Order.Open(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Reconstruct_PreservesTheOriginalId()
    {
        var id = Guid.NewGuid();

        var order = Order.Reconstruct(id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, OrderStatus.Open, items: []);

        Assert.Equal(id, order.Id);
        Assert.Equal(OrderStatus.Open, order.Status);
    }

    [Fact]
    public void Reconstruct_DerivesTotalFromTheGivenItems()
    {
        var items = new[]
        {
            new OrderItem(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2, 3m),
            new OrderItem(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, 4m),
        };

        var order = Order.Reconstruct(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, OrderStatus.Open, items);

        Assert.Equal(10m, order.Total);
    }

    [Fact]
    public void Reconstruct_PreservesTheGivenStatus()
    {
        // specs/restaurant/ordering/close-order.md BR6: a prior defect had Reconstruct silently
        // ignore whatever status it was given and always report Open - this pins the fix.
        var order = Order.Reconstruct(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, OrderStatus.Closed, items: []);

        Assert.Equal(OrderStatus.Closed, order.Status);
    }

    [Fact]
    public void Close_TransitionsAnOpenOrderToClosed()
    {
        var order = Order.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);

        var closed = order.Close();

        Assert.Equal(OrderStatus.Closed, closed.Status);
    }

    [Fact]
    public void Close_RejectsAnOrderThatIsNotOpen()
    {
        var order = Order.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow).Close();

        Assert.Throws<InvalidOperationException>(() => order.Close());
    }

    [Fact]
    public void Close_PreservesItemsAndTotal()
    {
        var items = new[] { new OrderItem(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2, 3m) };
        var order = Order.Reconstruct(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, OrderStatus.Open, items);

        var closed = order.Close();

        Assert.Equal(items, closed.Items);
        Assert.Equal(6m, closed.Total);
    }

    [Fact]
    public void Close_PreservesIdentityAndCreationFields()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var tableId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;
        var order = Order.Reconstruct(id, tenantId, tableId, userId, createdAt, OrderStatus.Open, items: []);

        var closed = order.Close();

        Assert.Equal(id, closed.Id);
        Assert.Equal(tenantId, closed.TenantId);
        Assert.Equal(tableId, closed.TableId);
        Assert.Equal(userId, closed.CreatedByUserId);
        Assert.Equal(createdAt, closed.CreatedAt);
    }
}
