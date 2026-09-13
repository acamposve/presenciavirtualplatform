using PresenciaVirtual.Modules.Restaurant.Settings;
using Xunit;

namespace PresenciaVirtual.Modules.Restaurant.Tests.Settings;

public class RestaurantSettingsTests
{
    [Fact]
    public void Configure_AssignsATenantScopedValue()
    {
        var tenantId = Guid.NewGuid();

        var settings = RestaurantSettings.Configure(tenantId, 5);

        Assert.Equal(tenantId, settings.TenantId);
        Assert.Equal(5, settings.MaxAlcoholicItemQuantityPerLine);
    }

    [Fact]
    public void Configure_AcceptsNull_MeaningNoLimit()
    {
        var settings = RestaurantSettings.Configure(Guid.NewGuid(), null);

        Assert.Null(settings.MaxAlcoholicItemQuantityPerLine);
    }

    [Fact]
    public void Configure_RejectsAnEmptyTenantId()
    {
        Assert.Throws<ArgumentException>(() => RestaurantSettings.Configure(Guid.Empty, 5));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Configure_RejectsAZeroOrNegativeValue(int value)
    {
        Assert.Throws<ArgumentException>(() => RestaurantSettings.Configure(Guid.NewGuid(), value));
    }

    [Fact]
    public void Configure_AcceptsThePositiveMaximum()
    {
        var settings = RestaurantSettings.Configure(Guid.NewGuid(), int.MaxValue);

        Assert.Equal(int.MaxValue, settings.MaxAlcoholicItemQuantityPerLine);
    }
}
