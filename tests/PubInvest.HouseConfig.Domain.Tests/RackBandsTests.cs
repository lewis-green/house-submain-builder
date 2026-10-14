using PubInvest.HouseConfig.Domain.Racks;

namespace PubInvest.HouseConfig.Domain.Tests;

public class RackBandsTests
{
    private static RackSpan At(int topU, int heightU = 1) => new(Guid.NewGuid(), topU, heightU);

    [Theory]
    [InlineData(4)]
    [InlineData(12)]
    [InlineData(14)]
    public void A_cabinet_of_14u_or_less_is_one_band(int heightU) =>
        Assert.Equal([new RackBand(heightU, 1)], RackBands.Split(heightU, [At(heightU)]));

    [Fact]
    public void A_42u_cabinet_is_three_bands_of_14_top_first() =>
        Assert.Equal(
            [new RackBand(42, 29), new RackBand(28, 15), new RackBand(14, 1)],
            RackBands.Split(42, [At(42), At(1)]));

    [Fact]
    public void A_22u_cabinet_is_a_full_band_then_the_rest() =>
        Assert.Equal([new RackBand(22, 9), new RackBand(8, 1)], RackBands.Split(22, []));

    /// A 2U panel in U29–U28 would be cut by the boundary at U29, so the
    /// boundary moves up and the panel is drawn whole in the band below.
    [Fact]
    public void An_item_across_a_boundary_moves_the_boundary_above_it() =>
        Assert.Equal(
            [new RackBand(42, 30), new RackBand(29, 16), new RackBand(15, 2), new RackBand(1, 1)],
            RackBands.Split(42, [At(29, 2)]));

    /// Only when the item sits at the top of its band and is taller than a
    /// band can the boundary not move up; the band grows to hold it whole.
    [Fact]
    public void An_item_taller_than_a_band_at_its_top_extends_the_band() =>
        Assert.Equal([new RackBand(4, 2), new RackBand(1, 1)], RackBands.Split(4, [At(4, 3)], maxBandU: 2));

    [Fact]
    public void The_bands_cover_every_u_once()
    {
        var bands = RackBands.Split(42, [At(40, 3), At(30, 2), At(15, 4), At(2, 2)]);

        var covered = bands.SelectMany(b => Enumerable.Range(b.BottomU, b.HeightU)).OrderBy(u => u);
        Assert.Equal(Enumerable.Range(1, 42), covered);
        Assert.All(bands, b => Assert.InRange(b.HeightU, 1, RackBands.MaxBandU));
    }
}
