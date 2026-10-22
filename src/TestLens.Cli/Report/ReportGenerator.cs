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
    private const string FontsPlaceholder = "/*__TESTLENS_FONTS__*/";

    // Ubuntu (Ubuntu Font Licence 1.0, latin subset), inlined so the report stays self-contained.
    private static readonly (string Family, int Weight, string File)[] Fonts =
    {
        ("Ubuntu", 400, "ubuntu-latin-400-normal.woff2"),
        ("Ubuntu", 500, "ubuntu-latin-500-normal.woff2"),
        ("Ubuntu", 700, "ubuntu-latin-700-normal.woff2"),
    };

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
            .Replace(VersionPlaceholder, toolVersion)
            .Replace(FontsPlaceholder, FontFaces());

        Directory.CreateDirectory(outputDir);
        var reportPath = Path.Combine(outputDir, "index.html");
        File.WriteAllText(reportPath, html);
        return reportPath;
    }

    private static string FontFaces()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var css = new System.Text.StringBuilder();
        foreach (var (family, weight, file) in Fonts)
        {
            using var stream = assembly.GetManifestResourceStream("TestLens.Fonts." + file)
                ?? throw new InvalidOperationException($"Embedded font missing: {file}");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            css.Append($"  @font-face {{ font-family: \"{family}\"; font-style: normal; font-weight: {weight}; font-display: swap; ")
               .Append($"src: url(data:font/woff2;base64,{Convert.ToBase64String(buffer.ToArray())}) format(\"woff2\"); }}\n");
        }
        return css.ToString();
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
