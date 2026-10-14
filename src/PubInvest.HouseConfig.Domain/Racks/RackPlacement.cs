namespace PubInvest.HouseConfig.Domain.Racks;

/// The U an item occupies, from BottomU up to TopU, both included. U1 is the
/// bottom of the cabinet.
public sealed record RackSpan(Guid ItemId, int TopU, int HeightU)
{
    public int BottomU => TopU - HeightU + 1;

    public bool Overlaps(int topU, int heightU) => topU - heightU + 1 <= TopU && BottomU <= topU;
}

/// Where items go in a rack. Pure, like the panel packer: the server calls it
/// for every add, move and cabinet change, and the UI only ever draws what the
/// server decided.
public static class RackPlacement
{
    /// The highest TopU where an item heightU tall fits on free U, or null when
    /// no run of free U is tall enough. Items fill from the top down, and a gap
    /// a removed item left near the top is filled before anything lower.
    public static int? NextTopU(int rackHeightU, int heightU, IReadOnlyCollection<RackSpan> occupied)
    {
        for (var top = rackHeightU; top - heightU + 1 >= 1; top--)
        {
            if (CanPlace(rackHeightU, top, heightU, occupied)) return top;
        }

        return null;
    }

    /// Inside the cabinet and on free U. An item being moved may overlap its
    /// own current place, so a one-U nudge of a 2U item is allowed.
    public static bool CanPlace(
        int rackHeightU,
        int topU,
        int heightU,
        IReadOnlyCollection<RackSpan> occupied,
        Guid? movingItemId = null)
        => heightU >= 1
           && topU <= rackHeightU
           && topU - heightU + 1 >= 1
           && occupied.All(s => s.ItemId == movingItemId || !s.Overlaps(topU, heightU));

    public static int FreeU(int rackHeightU, IReadOnlyCollection<RackSpan> occupied)
        => rackHeightU - occupied.Sum(s => s.HeightU);

    /// For a change to a shorter cabinet: the U holding items that would end up
    /// above the new top, from the lowest U of those items to the highest — or
    /// null when everything still fits. Items are never shuffled to make room;
    /// the person changing the cabinet is told what to move.
    public static (int From, int To)? Blocking(int newHeightU, IReadOnlyCollection<RackSpan> occupied)
    {
        var above = occupied.Where(s => s.TopU > newHeightU).ToList();
        return above.Count == 0 ? null : (above.Min(s => s.BottomU), above.Max(s => s.TopU));
    }
}
