using PresenciaVirtual.Modules.Restaurant.Ordering.AddItem;
using Xunit;

namespace PresenciaVirtual.Modules.Restaurant.Tests.Ordering.AddItem;

public class LineQuantityPolicyTests
{
    [Fact]
    public void WouldOverflow_ReturnsFalse_ForOrdinaryQuantities()
    {
        Assert.False(LineQuantityPolicy.WouldOverflow(existingQuantity: 4, requestedQuantity: 1));
    }

    [Fact]
    public void WouldOverflow_ReturnsFalse_WhenResultIsExactlyIntMaxValue()
    {
        Assert.False(LineQuantityPolicy.WouldOverflow(existingQuantity: int.MaxValue - 1, requestedQuantity: 1));
    }

    [Fact]
    public void WouldOverflow_ReturnsTrue_WhenResultWouldExceedIntMaxValue()
    {
        Assert.True(LineQuantityPolicy.WouldOverflow(existingQuantity: int.MaxValue - 1, requestedQuantity: 2));
    }
}
