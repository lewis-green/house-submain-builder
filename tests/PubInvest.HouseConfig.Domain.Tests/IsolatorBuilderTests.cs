using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Diagnostics;
using PubInvest.HouseConfig.Domain.Generation;

namespace PubInvest.HouseConfig.Domain.Tests;

public class IsolatorBuilderTests
{
    [Fact]
    public void Every_panel_gets_exactly_one_isolator()
    {
        var (device, diagnostics) = IsolatorBuilder.Build(CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        Assert.NotNull(device);
        Assert.Equal("Isolator", device!.Label);
        Assert.Equal(DeviceCategory.Isolator, device.Category);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void It_takes_its_width_from_the_catalogue()
    {
        var (device, _) = IsolatorBuilder.Build(CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        Assert.Equal(6, device!.ModuleWidth);
    }

    [Fact]
    public void A_missing_isolator_part_is_an_error_not_a_warning()
    {
        // A panel with no means of isolation is not one you would install.
        var catalogue = new DeviceCatalogue(CatalogueFixture.Catalogue().All
            .Where(d => d.Id != CatalogueFixture.IsolatorId));

        var (device, diagnostics) = IsolatorBuilder.Build(CatalogueFixture.Rules(), catalogue);

        Assert.Null(device);
        Assert.Equal(DiagnosticSeverity.Error, Assert.Single(diagnostics).Severity);
    }
}
