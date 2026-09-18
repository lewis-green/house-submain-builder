using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using PubInvest.HouseConfig.Domain.Circuits;
using PubInvest.HouseConfig.Domain.Generation;

namespace PubInvest.HouseConfig.Domain.Tests;

public class PanelGeneratorTests
{
    private static string SourceDirectory([CallerFilePath] string path = "")
        => Path.GetDirectoryName(path)!;

    private static readonly JsonSerializerOptions GoldenJson = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// Fixed ids keep the golden file stable across runs.
    private static Circuit Circuit(int n, CircuitType type, decimal? wPerM = null, decimal? metres = null) =>
        new(new Guid($"33333333-0000-0000-0000-{n:D12}"), type, $"{type} {n}", null, n, wPerM, metres);

    private static GenerationRequest TypicalSubmain()
    {
        var circuits = new List<Circuit>();
        for (var n = 1; n <= 5; n++) circuits.Add(Circuit(n, CircuitType.DimmedLighting));
        for (var n = 6; n <= 9; n++) circuits.Add(Circuit(n, CircuitType.Switched));
        for (var n = 10; n <= 11; n++) circuits.Add(Circuit(n, CircuitType.LedTape, 14.4m, 5m));

        return new GenerationRequest(
            circuits,
            CatalogueFixture.LargeEnclosure(),
            CatalogueFixture.Rules(),
            CatalogueFixture.Catalogue(),
            CatalogueFixture.AllEnclosures());
    }

    [Fact]
    public void A_typical_submain_generates_without_errors()
    {
        var result = PanelGenerator.Generate(TypicalSubmain());

        Assert.False(result.HasErrors);
        Assert.NotEmpty(result.Layout.Devices);
    }

    [Fact]
    public void Every_circuit_is_assigned_exactly_one_channel()
    {
        var request = TypicalSubmain();

        var result = PanelGenerator.Generate(request);

        var assigned = result.Layout.Devices
            .SelectMany(d => d.Channels)
            .Where(c => c.CircuitId is not null)
            .Select(c => c.CircuitId!.Value)
            .ToList();

        Assert.Equal(assigned.Distinct().Count(), assigned.Count);
        Assert.Equal(
            request.Circuits.Select(c => c.Id).OrderBy(id => id),
            assigned.OrderBy(id => id));
    }

    [Fact]
    public void The_external_driver_reaches_the_bom_but_never_the_rail()
    {
        // A bill of materials you could order from and still have no way to
        // light the tape would be worse than no bill at all.
        var result = PanelGenerator.Generate(TypicalSubmain());

        var driver = result.Bom.Lines.Single(l => l.CatalogueId == CatalogueFixture.Psu240Id);
        Assert.False(driver.PanelMounted);
        Assert.DoesNotContain(result.Layout.Devices, d => d.DeviceTypeId == CatalogueFixture.Psu240Id);
    }

    [Fact]
    public void Everything_on_the_rail_is_marked_panel_mounted()
    {
        var result = PanelGenerator.Generate(TypicalSubmain());

        foreach (var device in result.Layout.Devices)
        {
            var line = result.Bom.Lines.Single(l => l.CatalogueId == device.DeviceTypeId);
            Assert.True(line.PanelMounted, $"'{device.Label}' is on the rail but not marked panel-mounted");
        }
    }

    [Fact]
    public void Generation_is_deterministic()
    {
        var first = JsonSerializer.Serialize(PanelGenerator.Generate(TypicalSubmain()), GoldenJson);
        var second = JsonSerializer.Serialize(PanelGenerator.Generate(TypicalSubmain()), GoldenJson);

        Assert.Equal(first, second);
    }

    [Fact]
    public void A_typical_submain_matches_the_golden_layout()
    {
        var actual = JsonSerializer.Serialize(PanelGenerator.Generate(TypicalSubmain()), GoldenJson);

        var goldenPath = Path.Combine(AppContext.BaseDirectory, "Golden", "typical-submain.json");

        // Refreshing a golden file is deliberate, never automatic: run with
        // GOLDEN_UPDATE=1 and then read the diff before committing it.
        if (Environment.GetEnvironmentVariable("GOLDEN_UPDATE") == "1")
        {
            var source = Path.Combine(SourceDirectory(), "Golden", "typical-submain.json");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
            File.WriteAllText(source, actual);
            File.WriteAllText(goldenPath, actual);
        }

        if (!File.Exists(goldenPath))
        {
            throw new InvalidOperationException(
                $"Golden file missing. Review it, then re-run with GOLDEN_UPDATE=1:\n{actual}");
        }

        Assert.Equal(File.ReadAllText(goldenPath).ReplaceLineEndings(), actual.ReplaceLineEndings());
    }
}
