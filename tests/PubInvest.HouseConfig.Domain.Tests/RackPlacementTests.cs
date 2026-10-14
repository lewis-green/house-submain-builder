using PubInvest.HouseConfig.Domain.Racks;

namespace PubInvest.HouseConfig.Domain.Tests;

public class RackPlacementTests
{
    private static RackSpan At(int topU, int heightU = 1) => new(Guid.NewGuid(), topU, heightU);

    [Fact]
    public void The_first_item_goes_in_the_top_u() =>
        Assert.Equal(12, RackPlacement.NextTopU(12, 1, []));

    [Fact]
    public void The_next_item_goes_under_the_last() =>
        Assert.Equal(11, RackPlacement.NextTopU(12, 1, [At(12)]));

    [Fact]
    public void A_gap_left_by_a_removed_item_is_filled_first() =>
        Assert.Equal(12, RackPlacement.NextTopU(12, 1, [At(11), At(10)]));

    /// U12 is free but U11 is taken, so a 2U item cannot start at the top: it
    /// takes U10–U9, the highest run of two free U.
    [Fact]
    public void A_taller_item_needs_a_run_of_free_u_tall_enough() =>
        Assert.Equal(10, RackPlacement.NextTopU(12, 2, [At(11)]));

    [Fact]
    public void A_full_rack_has_no_room() =>
        Assert.Null(RackPlacement.NextTopU(2, 1, [At(2), At(1)]));

    [Fact]
    public void An_item_taller_than_any_free_run_has_no_room() =>
        Assert.Null(RackPlacement.NextTopU(3, 2, [At(2)]));

    [Theory]
    [InlineData(12, 1, true)]
    [InlineData(13, 1, false)]
    [InlineData(1, 1, true)]
    [InlineData(1, 2, false)]
    [InlineData(0, 1, false)]
    public void A_place_must_be_inside_the_cabinet(int topU, int heightU, bool allowed) =>
        Assert.Equal(allowed, RackPlacement.CanPlace(12, topU, heightU, []));

    [Fact]
    public void A_place_on_another_item_is_refused() =>
        Assert.False(RackPlacement.CanPlace(12, 5, 1, [At(5)]));

    [Fact]
    public void A_two_u_place_that_clips_another_item_is_refused() =>
        Assert.False(RackPlacement.CanPlace(12, 6, 2, [At(5)]));

    [Fact]
    public void A_move_that_overlaps_its_own_old_place_is_allowed()
    {
        var self = At(5, 2);
        Assert.True(RackPlacement.CanPlace(12, 6, 2, [self], self.ItemId));
    }

    [Fact]
    public void Free_u_counts_what_is_left() =>
        Assert.Equal(9, RackPlacement.FreeU(12, [At(12), At(11, 2)]));

    /// The whole of each item above the new top has to go, so the range starts
    /// at the lowest U of those items, not at the new top.
    [Fact]
    public void Shrinking_past_items_names_the_u_to_clear() =>
        Assert.Equal((10, 12), RackPlacement.Blocking(9, [At(12), At(11, 2), At(3)]));

    [Fact]
    public void Shrinking_with_everything_at_or_below_the_new_top_is_fine() =>
        Assert.Null(RackPlacement.Blocking(9, [At(9), At(3)]));
}
