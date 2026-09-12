using PresenciaVirtual.Modules.Restaurant.Tables;
using Xunit;

namespace PresenciaVirtual.Modules.Restaurant.Tests.Tables;

public class TableTests
{
    [Fact]
    public void Register_AssignsATenantScopedIdentity()
    {
        var tenantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var table = Table.Register(tenantId, "Table 5", now);

        Assert.NotEqual(Guid.Empty, table.Id);
        Assert.Equal(tenantId, table.TenantId);
        Assert.Equal("Table 5", table.Label);
        Assert.Equal(now, table.CreatedAt);
    }

    [Fact]
    public void Register_GeneratesADifferentIdEachTime()
    {
        var tenantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var first = Table.Register(tenantId, "Table 1", now);
        var second = Table.Register(tenantId, "Table 2", now);

        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void Register_RejectsAnEmptyTenantId()
    {
        Assert.Throws<ArgumentException>(() => Table.Register(Guid.Empty, "Table 5", DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_RejectsAnEmptyOrWhitespaceLabel(string label)
    {
        Assert.Throws<ArgumentException>(() => Table.Register(Guid.NewGuid(), label, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Register_AcceptsALabelAtTheMaximumLength()
    {
        var label = new string('a', Table.MaxLabelLength);

        var table = Table.Register(Guid.NewGuid(), label, DateTimeOffset.UtcNow);

        Assert.Equal(label, table.Label);
    }

    [Fact]
    public void Register_RejectsALabelLongerThanTheMaximum()
    {
        var label = new string('a', Table.MaxLabelLength + 1);

        Assert.Throws<ArgumentException>(() => Table.Register(Guid.NewGuid(), label, DateTimeOffset.UtcNow));
    }
}
