using System.Globalization;
using System.Text;
using PubInvest.HouseConfig.Domain.Bom;

namespace PubInvest.HouseConfig.Api.Documents;

public static class BomCsv
{
    public static string Write(BillOfMaterials bom, string title)
    {
        var csv = new StringBuilder();
        csv.AppendLine(Quote(title));
        csv.AppendLine("Part,Description,Quantity,Panel mounted");

        foreach (var line in bom.Lines)
        {
            csv.Append(Quote(line.PartNumber)).Append(',')
               .Append(Quote(line.Description)).Append(',')
               .Append(line.Quantity.ToString(CultureInfo.InvariantCulture)).Append(',')
               .Append(line.PanelMounted ? "yes" : "no")
               .AppendLine();
        }

        return csv.ToString();
    }

    private static string Quote(string value)
    {
        var needsQuoting = value.Contains(',') || value.Contains('"') || value.Contains('\n');
        if (!needsQuoting) return value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
