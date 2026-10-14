namespace PubInvest.HouseConfig.Domain.Racks;

/// A run of U printed together, from BottomU up to TopU, both included.
public sealed record RackBand(int TopU, int BottomU)
{
    public int HeightU => TopU - BottomU + 1;
}

/// How a tall cabinet is cut up for printing. A 42U cabinet drawn whole on a
/// portrait page comes out at an eighth of the scale of a 12U one, with port
/// squares under a millimetre; drawn in bands of at most 14U, every band is as
/// legible as a wall cabinet.
public static class RackBands
{
    /// The tallest band: a 14U band fills the width of an A4 page and just fits
    /// its height, so bands never have to be shrunk.
    public const int MaxBandU = 14;

    /// Top band first. No item is ever cut by a band boundary: where one would
    /// be, the boundary moves up so the item is drawn whole in the band below,
    /// or, when the item is at the top of its band and taller than a band, the
    /// band grows to hold it.
    public static IReadOnlyList<RackBand> Split(int rackHeightU, IReadOnlyCollection<RackSpan> occupied, int maxBandU = MaxBandU)
    {
        var bands = new List<RackBand>();

        for (var top = rackHeightU; top >= 1;)
        {
            var bottom = Math.Max(1, top - maxBandU + 1);

            var straddler = occupied.FirstOrDefault(s => s.TopU <= top && s.TopU >= bottom && s.BottomU < bottom);
            if (straddler is not null)
            {
                bottom = straddler.TopU < top ? straddler.TopU + 1 : straddler.BottomU;
            }

            bands.Add(new RackBand(top, bottom));
            top = bottom - 1;
        }

        return bands;
    }
}
