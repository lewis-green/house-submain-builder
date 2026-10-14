using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Api.Contracts;
using PubInvest.HouseConfig.Data;
using PubInvest.HouseConfig.Domain.Racks;

namespace PubInvest.HouseConfig.Api.Racks;

/// What a rack catalogue row must be before it is saved. A row that breaks
/// these would only fail later, on site: a port group with a gap renumbers a
/// schedule, and an accessory that is not one never reaches the parts list.
public static class RackCatalogueRules
{
    public static Dictionary<string, string[]> Validate(SaveRackTypeRequest request)
    {
        var problems = new Dictionary<string, string[]>();
        Required(problems, "manufacturer", request.Manufacturer);
        Required(problems, "model", request.Model);
        Required(problems, "partNumber", request.PartNumber);

        if (request.HeightU is < 1 or > 60) problems["heightU"] = ["A cabinet is between 1U and 60U tall."];
        if (request.Mounting is not ("Wall" or "Floor")) problems["mounting"] = ["Mounting is Wall or Floor."];
        return problems;
    }

    public static async Task<Dictionary<string, string[]>> ValidateAsync(
        HouseConfigDbContext db,
        Guid id,
        SaveRackDeviceTypeRequest request,
        CancellationToken ct)
    {
        var problems = new Dictionary<string, string[]>();
        Required(problems, "manufacturer", request.Manufacturer);
        Required(problems, "model", request.Model);
        Required(problems, "partNumber", request.PartNumber);

        if (!Enum.TryParse<RackDeviceKind>(request.Kind, out var kind))
        {
            problems["kind"] = ["Kind is Switch, PatchPanel or Accessory."];
            return problems;
        }

        if (kind == RackDeviceKind.Accessory)
        {
            if (request.HeightU != 0 || request.PortGroups.Count > 0 || request.RequiredAccessoryId is not null)
            {
                problems["kind"] = ["An accessory takes no rack space, has no ports and needs nothing else."];
            }
        }
        else
        {
            if (request.HeightU is < 1 or > 10) problems["heightU"] = ["A rack item is between 1U and 10U tall."];

            var next = 1;
            foreach (var group in request.PortGroups)
            {
                if (string.IsNullOrWhiteSpace(group.Name) || group.From != next || group.To < group.From)
                {
                    problems["portGroups"] = [$"Ports run from 1 with no gaps or overlaps: the next group should start at port {next}."];
                    break;
                }
                next = group.To + 1;
            }

            if (request.PortGroups.Count == 0) problems["portGroups"] = ["A switch or patch panel needs at least one port group."];
        }

        if (request.RequiredAccessoryId is { } accessoryId
            && !await db.RackDeviceTypes.AnyAsync(t => t.Id == accessoryId && t.Kind == nameof(RackDeviceKind.Accessory), ct))
        {
            problems["requiredAccessoryId"] = ["The required accessory must be an accessory in the catalogue."];
        }

        var partNumber = request.PartNumber.Trim();
        if (await db.RackDeviceTypes.AnyAsync(t => t.Id != id && t.PartNumber == partNumber, ct))
        {
            problems["partNumber"] = [$"'{partNumber}' is already in the catalogue."];
        }

        return problems;
    }

    private static void Required(Dictionary<string, string[]> problems, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) problems[key] = ["This is required."];
    }
}
