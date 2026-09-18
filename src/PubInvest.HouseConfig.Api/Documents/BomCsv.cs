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
        csv.AppendLine("Part,Description,Quantity,Unit cost,Line total");

        foreach (var line in bom.Lines)
        {
            csv.Append(Quote(line.PartNumber)).Append(',')
               .Append(Quote(line.Description)).Append(',')
               .Append(line.Quantity.ToString(CultureInfo.InvariantCulture)).Append(',')
               // An unpriced part leaves the cost cells empty. A literal 0.00
               // would read as "this part is free", which is a different claim.
               .Append(line.UnitCost <= 0m ? "" : line.UnitCost.ToString("0.00", CultureInfo.InvariantCulture))
               .Append(',')
               .Append(line.UnitCost <= 0m ? "" : line.LineTotal.ToString("0.00", CultureInfo.InvariantCulture))
               .AppendLine();
        }

        var unpriced = bom.Lines.Count(l => l.UnitCost <= 0m);
        csv.AppendLine(unpriced > 0
            ? $",,,,{Quote($"Not priced ({unpriced} of {bom.Lines.Count} parts have no cost)")}"
            : $",,,Total,{bom.Total.ToString("0.00", CultureInfo.InvariantCulture)}");

        return csv.ToString();
    }

    private static string Quote(string value)
    {
        var needsQuoting = value.Contains(',') || value.Contains('"') || value.Contains('\n');
        if (!needsQuoting) return value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
