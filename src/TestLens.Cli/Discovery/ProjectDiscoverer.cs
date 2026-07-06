using System.Text.Json;
using System.Text.RegularExpressions;
using TestLens.Models;

namespace TestLens.Discovery;

public sealed record DiscoveredProject(
    string Name,
    string AbsolutePath,
    string RelativePath,
    string Kind,
    string Framework);

/// <summary>
/// Walks a directory tree and finds test projects:
/// C# projects referencing a test framework, and Vue/Angular/JS packages
/// with a test runner in their dependencies.
/// </summary>
public static class ProjectDiscoverer
{
    private static readonly string[] SkippedDirs =
    {
        "node_modules", "bin", "obj", "dist", ".git", ".testlens", ".angular",
        ".nuget", "packages", "coverage", ".vs", ".idea", "TestResults",
    };

    public static List<DiscoveredProject> Discover(string root)
    {
        var results = new List<DiscoveredProject>();
        Walk(new DirectoryInfo(root), root, results);
        return results
            .OrderBy(p => p.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void Walk(DirectoryInfo dir, string root, List<DiscoveredProject> results)
    {
        FileInfo[] files;
        try { files = dir.GetFiles(); }
        catch (UnauthorizedAccessException) { return; }

        foreach (var file in files)
        {
            if (file.Extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                var project = InspectCsproj(file, root);
                if (project is not null) results.Add(project);
            }
            else if (file.Name.Equals("package.json", StringComparison.OrdinalIgnoreCase))
            {
                var project = InspectPackageJson(file, root);
                if (project is not null) results.Add(project);
            }
        }

        foreach (var sub in dir.GetDirectories())
        {
            if (SkippedDirs.Contains(sub.Name, StringComparer.OrdinalIgnoreCase)) continue;
            if (sub.Name.StartsWith('.')) continue;
            Walk(sub, root, results);
        }
    }

    private static DiscoveredProject? InspectCsproj(FileInfo file, string root)
    {
        string content;
        try { content = File.ReadAllText(file.FullName); }
        catch (IOException) { return null; }

        string framework =
            Regex.IsMatch(content, @"""xunit[""\.]", RegexOptions.IgnoreCase) ? "xunit" :
            Regex.IsMatch(content, @"""nunit""", RegexOptions.IgnoreCase) ? "nunit" :
            Regex.IsMatch(content, @"""MSTest(\.TestFramework)?""", RegexOptions.IgnoreCase) ? "mstest" :
            "";

        // A csproj referencing only Microsoft.NET.Test.Sdk still counts as a test project.
        bool isTestProject = framework.Length > 0
            || content.Contains("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(content, @"<IsTestProject>\s*true", RegexOptions.IgnoreCase);

        if (!isTestProject) return null;

        return new DiscoveredProject(
            Name: Path.GetFileNameWithoutExtension(file.Name),
            AbsolutePath: file.DirectoryName!,
            RelativePath: Path.GetRelativePath(root, file.DirectoryName!).Replace('\\', '/'),
            Kind: "csharp",
            Framework: framework.Length > 0 ? framework : "unknown");
    }

    private static DiscoveredProject? InspectPackageJson(FileInfo file, string root)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(File.ReadAllText(file.FullName)); }
        catch (Exception e) when (e is IOException or JsonException) { return null; }

        using (doc)
        {
            var deps = CollectDependencies(doc.RootElement);
            if (deps.Count == 0) return null;

            string kind =
                deps.Any(d => d == "vue" || d.StartsWith("@vue/")) ? "vue" :
                deps.Any(d => d.StartsWith("@angular/")) ? "angular" :
                "javascript";

            string framework =
                deps.Contains("@playwright/test") ? "playwright" :
                deps.Contains("vitest") ? "vitest" :
                deps.Contains("jest") || deps.Contains("jest-preset-angular") ? "jest" :
                deps.Contains("karma") || deps.Contains("jasmine-core") ? "karma-jasmine" :
                deps.Contains("mocha") ? "mocha" :
                "";

            // Only Vue/Angular/JS packages that actually have a test setup are interesting.
            // Playwright projects are always interesting - their "test" script is often
            // named differently (e.g. "test:e2e"), so we don't require one.
            if (framework.Length == 0) return null;
            if (kind == "javascript" && framework != "playwright" && !HasTestScript(doc.RootElement)) return null;

            string name = doc.RootElement.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                ? n.GetString()!
                : new DirectoryInfo(file.DirectoryName!).Name;

            return new DiscoveredProject(
                Name: name,
                AbsolutePath: file.DirectoryName!,
                RelativePath: Path.GetRelativePath(root, file.DirectoryName!).Replace('\\', '/'),
                Kind: kind,
                Framework: framework);
        }
    }

    private static HashSet<string> CollectDependencies(JsonElement rootElement)
    {
        var deps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in new[] { "dependencies", "devDependencies" })
        {
            if (rootElement.TryGetProperty(section, out var obj) && obj.ValueKind == JsonValueKind.Object)
                foreach (var prop in obj.EnumerateObject())
                    deps.Add(prop.Name);
        }
        return deps;
    }

    private static bool HasTestScript(JsonElement rootElement) =>
        rootElement.TryGetProperty("scripts", out var scripts)
        && scripts.ValueKind == JsonValueKind.Object
        && scripts.TryGetProperty("test", out _);
}
