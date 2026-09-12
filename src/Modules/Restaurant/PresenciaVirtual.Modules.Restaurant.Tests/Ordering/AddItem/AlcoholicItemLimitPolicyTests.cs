using PresenciaVirtual.Modules.Restaurant.Ordering.AddItem;
using Xunit;

namespace PresenciaVirtual.Modules.Restaurant.Tests.Ordering.AddItem;

public class AlcoholicItemLimitPolicyTests
{
    [Fact]
    public void Exceeds_ReturnsFalse_WhenNoLimitIsConfigured()
    {
        Assert.False(AlcoholicItemLimitPolicy.Exceeds(existingQuantity: 100, requestedQuantity: 100, maxQuantityPerLine: null));
    }

    [Fact]
    public void Exceeds_ReturnsFalse_WhenResultIsExactlyAtTheLimit()
    {
        Assert.False(AlcoholicItemLimitPolicy.Exceeds(existingQuantity: 4, requestedQuantity: 1, maxQuantityPerLine: 5));
    }

    [Fact]
    public void Exceeds_ReturnsTrue_WhenResultWouldGoAboveTheLimit()
    {
        Assert.True(AlcoholicItemLimitPolicy.Exceeds(existingQuantity: 4, requestedQuantity: 2, maxQuantityPerLine: 5));
    }

    [Fact]
    public void Exceeds_ReturnsFalse_ForAFirstAddWellBelowTheLimit()
    {
        Assert.False(AlcoholicItemLimitPolicy.Exceeds(existingQuantity: 0, requestedQuantity: 1, maxQuantityPerLine: 5));
    }
}
