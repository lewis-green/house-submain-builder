using PubInvest.HouseConfig.Domain.Bom;
using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Circuits;
using PubInvest.HouseConfig.Domain.Diagnostics;
using PubInvest.HouseConfig.Domain.Layout;
using PubInvest.HouseConfig.Domain.Rules;

namespace PubInvest.HouseConfig.Domain.Generation;

public sealed record GenerationRequest(
    IReadOnlyList<Circuit> Circuits,
    EnclosureType Enclosure,
    RuleSetPayload Rules,
    DeviceCatalogue Catalogue,
    IReadOnlyList<EnclosureType> AllEnclosures,
    IReadOnlyList<PositionOverride>? Overrides = null,
    /// False for a submain isolated upstream: no isolator is fitted and none is
    /// asked for.
    bool IncludeIsolator = true,
    /// True when the enclosure is glanded from below, so the terminations belong
    /// on the bottom rail rather than the top.
    bool TerminalsAtBottom = false);

public sealed record GenerationResult(
    PanelLayout Layout,
    IReadOnlyList<Diagnostic> Diagnostics,
    BillOfMaterials Bom)
{
    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
}

public static class PanelGenerator
{
    public static GenerationResult Generate(GenerationRequest request)
    {
        var diagnostics = new List<Diagnostic>();

        RequiredDevice? isolator = null;

        if (request.IncludeIsolator)
        {
            var built = IsolatorBuilder.Build(request.Rules, request.Catalogue);
            isolator = built.Device;
            diagnostics.AddRange(built.Diagnostics);
        }

        var terminals = TerminalBandBuilder.Build(request.Circuits, request.Rules, request.Catalogue);
        diagnostics.AddRange(terminals.Diagnostics);

        var demand = DeviceDemandCalculator.Calculate(request.Circuits, request.Rules, request.Catalogue);
        diagnostics.AddRange(demand.Diagnostics);

        var tape = TapeSupplySizer.Size(request.Circuits, request.Rules, request.Catalogue);
        diagnostics.AddRange(tape.Diagnostics);

        // The isolator leads: it is the first thing on the termination row.
        var allDevices = (isolator is null ? Enumerable.Empty<RequiredDevice>() : [isolator])
            .Concat(terminals.Devices)
            .Concat(demand.Devices)
            .Concat(tape.Blocks)
            .ToList();

        var packed = PanelPacker.Pack(
            allDevices, request.Enclosure, request.Rules, request.AllEnclosures, request.TerminalsAtBottom);
        diagnostics.AddRange(packed.Diagnostics);

        // Overrides run after packing and before the BOM, so the parts list counts
        // the same devices the drawing shows.
        var (layout, overrideDiagnostics) =
            OverrideApplier.Apply(packed.Layout, request.Overrides ?? []);
        diagnostics.AddRange(overrideDiagnostics);

        // External parts are costed but never placed.
        var offRail = terminals.Accessories.Concat(tape.ExternalParts).ToList();
        var bom = BomBuilder.Build(layout, offRail, request.Enclosure, request.Catalogue);

        return new GenerationResult(layout, diagnostics, bom);
    }
}
