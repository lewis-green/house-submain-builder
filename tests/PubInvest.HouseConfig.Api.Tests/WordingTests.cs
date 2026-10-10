using PubInvest.HouseConfig.Api.Documents;
using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Circuits;

namespace PubInvest.HouseConfig.Api.Tests;

/// The PDF prints these words; a value with no entry would print its enum name
/// ("Dimmer240", "DimmedLighting"), which nobody on site says.
public class WordingTests
{
    [Theory]
    [MemberData(nameof(Categories))]
    public void Every_device_category_has_a_human_kind(DeviceCategory category)
    {
        Assert.True(Wording.HasKind(category), $"{category} has no wording");
        Assert.DoesNotMatch(@"\d|_", Wording.Kind(category).Replace("+24V", "").Replace("-24V", ""));
    }

    [Theory]
    [MemberData(nameof(CircuitTypes))]
    public void Every_circuit_type_has_a_human_name(CircuitType type)
    {
        Assert.True(Wording.HasCircuitType(type), $"{type} has no wording");
        // No CamelCase on paper: a space, or a single word, like the wizard says it.
        Assert.DoesNotMatch("[a-z][A-Z]", Wording.CircuitType(type.ToString()));
    }

    [Fact]
    public void A_blinds_controller_is_described_as_blinds_as_the_wizard_says()
    {
        Assert.Equal("Blinds", Wording.Kind(DeviceCategory.Cover));
        Assert.Equal("Blinds", Wording.CircuitType("Cover"));
    }

    [Fact]
    public void An_unknown_circuit_type_string_is_printed_as_it_is_rather_than_failing()
    {
        Assert.Equal("Whatever", Wording.CircuitType("Whatever"));
    }

    public static TheoryData<DeviceCategory> Categories()
    {
        var data = new TheoryData<DeviceCategory>();
        foreach (var category in Enum.GetValues<DeviceCategory>()) data.Add(category);
        return data;
    }

    public static TheoryData<CircuitType> CircuitTypes()
    {
        var data = new TheoryData<CircuitType>();
        foreach (var type in Enum.GetValues<CircuitType>()) data.Add(type);
        return data;
    }
}
