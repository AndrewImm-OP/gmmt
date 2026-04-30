using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gmmt.Report;

public static class ReportJsonWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string ToJson(PatchReport report)
    {
        return JsonSerializer.Serialize(report, Options);
    }

    public static void WriteToFile(PatchReport report, string path)
    {
        var json = ToJson(report);
        File.WriteAllText(path, json);
    }
}
