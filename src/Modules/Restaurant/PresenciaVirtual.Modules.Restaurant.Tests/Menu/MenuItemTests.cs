using PresenciaVirtual.Modules.Restaurant.Menu;
using Xunit;

namespace PresenciaVirtual.Modules.Restaurant.Tests.Menu;

public class MenuItemTests
{
    [Fact]
    public void Register_AssignsATenantScopedIdentity()
    {
        var tenantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var menuItem = MenuItem.Register(tenantId, "Coke", 3m, isAlcoholic: false, now);

        Assert.NotEqual(Guid.Empty, menuItem.Id);
        Assert.Equal(tenantId, menuItem.TenantId);
        Assert.Equal("Coke", menuItem.Name);
        Assert.Equal(3m, menuItem.Price);
        Assert.False(menuItem.IsAlcoholic);
        Assert.Equal(now, menuItem.CreatedAt);
    }

    [Fact]
    public void Register_GeneratesADifferentIdEachTime()
    {
        var tenantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var first = MenuItem.Register(tenantId, "Coke", 3m, isAlcoholic: false, now);
        var second = MenuItem.Register(tenantId, "Sprite", 3m, isAlcoholic: false, now);

        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void Register_RejectsAnEmptyTenantId()
    {
        Assert.Throws<ArgumentException>(() => MenuItem.Register(Guid.Empty, "Coke", 3m, isAlcoholic: false, DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_RejectsAnEmptyOrWhitespaceName(string name)
    {
        Assert.Throws<ArgumentException>(() => MenuItem.Register(Guid.NewGuid(), name, 3m, isAlcoholic: false, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Register_AcceptsANameAtTheMaximumLength()
    {
        var name = new string('a', MenuItem.MaxNameLength);

        var menuItem = MenuItem.Register(Guid.NewGuid(), name, 3m, isAlcoholic: false, DateTimeOffset.UtcNow);

        Assert.Equal(name, menuItem.Name);
    }

    [Fact]
    public void Register_RejectsANameLongerThanTheMaximum()
    {
        var name = new string('a', MenuItem.MaxNameLength + 1);

        Assert.Throws<ArgumentException>(() => MenuItem.Register(Guid.NewGuid(), name, 3m, isAlcoholic: false, DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Register_RejectsAZeroOrNegativePrice(decimal price)
    {
        Assert.Throws<ArgumentException>(() => MenuItem.Register(Guid.NewGuid(), "Coke", price, isAlcoholic: false, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Register_AcceptsThePriceAtTheMaximum()
    {
        var menuItem = MenuItem.Register(Guid.NewGuid(), "Coke", MenuItem.MaxPrice, isAlcoholic: false, DateTimeOffset.UtcNow);

        Assert.Equal(MenuItem.MaxPrice, menuItem.Price);
    }

    [Fact]
    public void Register_RejectsAPriceAboveTheMaximum()
    {
        Assert.Throws<ArgumentException>(() => MenuItem.Register(Guid.NewGuid(), "Coke", MenuItem.MaxPrice + 0.01m, isAlcoholic: false, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Register_RejectsAPriceWithMoreThanTwoDecimalPlaces()
    {
        Assert.Throws<ArgumentException>(() => MenuItem.Register(Guid.NewGuid(), "Coke", 1.999m, isAlcoholic: false, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Register_AcceptsAPriceWithExactlyTwoDecimalPlaces()
    {
        var menuItem = MenuItem.Register(Guid.NewGuid(), "Coke", 3.99m, isAlcoholic: false, DateTimeOffset.UtcNow);

        Assert.Equal(3.99m, menuItem.Price);
    }

    [Fact]
    public void Register_AcceptsIsAlcoholicTrue()
    {
        // BR4's "defaults to false when not supplied" is a request-DTO concern (the API layer
        // supplies false when the caller omits the field) - Register itself always takes an
        // explicit value, which this pins down for the true case (false is already covered by
        // Register_AssignsATenantScopedIdentity above).
        var menuItem = MenuItem.Register(Guid.NewGuid(), "Beer", 5m, isAlcoholic: true, DateTimeOffset.UtcNow);

        Assert.True(menuItem.IsAlcoholic);
    }
}
