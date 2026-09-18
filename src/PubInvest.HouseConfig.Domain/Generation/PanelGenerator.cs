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
    IReadOnlyList<PositionOverride>? Overrides = null);

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

        var terminals = TerminalBandBuilder.Build(request.Circuits, request.Rules, request.Catalogue);
        diagnostics.AddRange(terminals.Diagnostics);

        var demand = DeviceDemandCalculator.Calculate(request.Circuits, request.Rules, request.Catalogue);
        diagnostics.AddRange(demand.Diagnostics);

        var psus = PsuSizer.Size(request.Circuits, request.Rules, request.Catalogue);
        diagnostics.AddRange(psus.Diagnostics);

        var allDevices = terminals.Devices
            .Concat(demand.Devices)
            .Concat(psus.Devices)
            .ToList();

        var packed = BandPacker.Pack(allDevices, request.Enclosure, request.Rules, request.AllEnclosures);
        diagnostics.AddRange(packed.Diagnostics);

        // Overrides run after packing and before the BOM, so the parts list counts
        // the same devices the drawing shows.
        var (layout, overrideDiagnostics) =
            OverrideApplier.Apply(packed.Layout, request.Overrides ?? []);
        diagnostics.AddRange(overrideDiagnostics);

        var bom = BomBuilder.Build(layout, terminals.Accessories, request.Enclosure, request.Catalogue);

        return new GenerationResult(layout, diagnostics, bom);
    }
}
