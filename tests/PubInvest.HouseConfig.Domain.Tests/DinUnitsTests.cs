using PubInvest.HouseConfig.Domain.Catalogue;

namespace PubInvest.HouseConfig.Domain.Tests;

public class DinUnitsTests
{
    [Fact]
    public void Three_slot_units_make_one_din_module()
    {
        Assert.Equal(3, DinUnits.PerModule);
        Assert.Equal(1m, DinUnits.ToModules(3));
        Assert.Equal(36, DinUnits.FromModules(12));
    }

    [Fact]
    public void A_single_terminal_block_is_a_third_of_a_module()
    {
        Assert.Equal(1m / 3m, DinUnits.ToModules(1), precision: 6);
    }
}
