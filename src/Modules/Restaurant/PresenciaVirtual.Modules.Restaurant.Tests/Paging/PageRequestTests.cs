using PresenciaVirtual.Modules.Restaurant.Paging;
using Xunit;

namespace PresenciaVirtual.Modules.Restaurant.Tests.Paging;

public class PageRequestTests
{
    [Fact]
    public void Resolve_DefaultsToTheFirstPageAndFiftyItems()
    {
        Assert.Equal(1, PageRequest.ResolvePage(null));
        Assert.Equal(PageRequest.DefaultPageSize, PageRequest.ResolvePageSize(null));
    }

    [Fact]
    public void Resolve_DefaultsEachValueIndependently()
    {
        // FR5: supplying only one of the two must not reset the other to nothing.
        Assert.Equal(1, PageRequest.ResolvePage(null));
        Assert.Equal(200, PageRequest.ResolvePageSize(200));
        Assert.Equal(3, PageRequest.ResolvePage(3));
        Assert.Equal(PageRequest.DefaultPageSize, PageRequest.ResolvePageSize(null));
    }

    [Fact]
    public void Offset_IsZeroBasedOnTheRequestedPage()
    {
        Assert.Equal(0L, PageRequest.Offset(1, 50));
        Assert.Equal(50L, PageRequest.Offset(2, 50));
        Assert.Equal(200L, PageRequest.Offset(3, 100));
    }

    [Fact]
    public void Offset_CannotOverflowIntoANegativeValueForALargePageNumber()
    {
        // Computed in 64-bit precisely so a large (individually valid) page number cannot wrap
        // around and silently serve the first page again.
        var offset = PageRequest.Offset(int.MaxValue, PageRequest.MaxPageSize);

        Assert.True(offset > 0, $"offset was {offset}");
        Assert.Equal(((long)int.MaxValue - 1) * 200, offset);
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(-1, 50)]
    [InlineData(1, 0)]
    [InlineData(1, -5)]
    [InlineData(1, 201)]
    public void Validate_RejectsOutOfRangeValues(int page, int pageSize)
    {
        var errors = PageRequest.Validate(page, pageSize);

        Assert.NotEmpty(errors);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 50)]
    [InlineData(1, 200)]
    [InlineData(int.MaxValue, 200)]
    public void Validate_AcceptsInRangeValuesIncludingBothCeilings(int page, int pageSize)
    {
        // The `pageSize` ceiling must be inclusive, and no upper bound is imposed on `page`
        // (a page past the end is a valid request that returns an empty list).
        Assert.Empty(PageRequest.Validate(page, pageSize));
    }
}
