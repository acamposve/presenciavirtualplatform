using PresenciaVirtual.Modules.Restaurant.Menu.GetMenuItems;
using PresenciaVirtual.Modules.Restaurant.Tables.ListTables;
using Xunit;

namespace PresenciaVirtual.Modules.Restaurant.Tests.Paging;

/// <summary>
/// Both read capabilities are specified to share one paging contract
/// (specs/restaurant/menu/get-menu-items.md FR4/FR5 and
/// specs/restaurant/tables/list-tables.md FR4/FR5); these pin that their validators actually
/// surface the shared rules rather than silently accepting everything.
/// </summary>
public class ReadCapabilityValidatorTests
{
    [Fact]
    public void GetMenuItemsValidator_RejectsAPageSizeAboveTheCeiling()
    {
        Assert.NotEmpty(GetMenuItemsValidator.Validate(new GetMenuItemsQuery(1, 201)));
    }

    [Fact]
    public void GetMenuItemsValidator_AcceptsTheCeiling()
    {
        Assert.Empty(GetMenuItemsValidator.Validate(new GetMenuItemsQuery(1, 200)));
    }

    [Fact]
    public void ListTablesValidator_RejectsAPageBelowOne()
    {
        Assert.NotEmpty(ListTablesValidator.Validate(new ListTablesQuery(0, 50)));
    }

    [Fact]
    public void ListTablesValidator_AcceptsAValidQuery()
    {
        Assert.Empty(ListTablesValidator.Validate(new ListTablesQuery(2, 25)));
    }
}
