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

        var order = Order.Reconstruct(id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(id, order.Id);
        Assert.Equal(OrderStatus.Open, order.Status);
    }
}
