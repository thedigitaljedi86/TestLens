using System.Reflection;
using System.Text.Json;
using TestLens.Models;

namespace TestLens.Report;

/// <summary>
/// Produces the self-contained HTML dashboard by injecting the run history
/// into the embedded report template.
/// </summary>
public static class ReportGenerator
{
    private const string DataPlaceholder = "/*__TESTLENS_DATA__*/";
    private const string VersionPlaceholder = "__TESTLENS_VERSION__";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Generate(List<RunSnapshot> runs, string outputDir, string toolVersion)
    {
        var template = ReadTemplate();
        var payload = JsonSerializer.Serialize(runs, JsonOptions)
            .Replace("</", "<\\/"); // keep the inline <script> block safe

        var html = template
            .Replace(DataPlaceholder, $"window.TESTLENS_RUNS = {payload};")
            .Replace(VersionPlaceholder, toolVersion);

        Directory.CreateDirectory(outputDir);
        var reportPath = Path.Combine(outputDir, "index.html");
        File.WriteAllText(reportPath, html);
        return reportPath;
    }

    private static string ReadTemplate()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith("report.html", StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
