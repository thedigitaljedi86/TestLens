using TestLens.Discovery;
using TestLens.History;
using TestLens.Models;
using TestLens.Report;
using Xunit;

namespace TestLens.Cli.Tests;

public class ProjectDiscovererTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("testlens-disc").FullName;
    public void Dispose() => Directory.Delete(_root, true);

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void Finds_csharp_test_projects_and_their_framework()
    {
        Write("Api.Tests/Api.Tests.csproj", """
            <Project><ItemGroup>
              <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.0.0" />
              <PackageReference Include="xunit" Version="2.9.0" />
            </ItemGroup></Project>
            """);
        Write("Api/Api.csproj", "<Project><ItemGroup></ItemGroup></Project>");

        var projects = ProjectDiscoverer.Discover(_root);

        var project = Assert.Single(projects);
        Assert.Equal("Api.Tests", project.Name);
        Assert.Equal("csharp", project.Kind);
        Assert.Equal("xunit", project.Framework);
    }

    [Fact]
    public void Classifies_vue_and_angular_packages()
    {
        Write("shop/package.json", """{"name":"shop","dependencies":{"vue":"^3.0.0"},"devDependencies":{"vitest":"^2.0.0"}}""");
        Write("admin/package.json", """{"name":"admin","dependencies":{"@angular/core":"^18.0.0"},"devDependencies":{"karma":"^6.0.0","jasmine-core":"^5.0.0"}}""");

        var projects = ProjectDiscoverer.Discover(_root);

        Assert.Equal(2, projects.Count);
        Assert.Contains(projects, p => p.Kind == "vue" && p.Framework == "vitest");
        Assert.Contains(projects, p => p.Kind == "angular" && p.Framework == "karma-jasmine");
    }

    [Fact]
    public void Detects_playwright_projects_even_without_a_test_script()
    {
        Write("e2e/package.json", """{"name":"e2e","devDependencies":{"@playwright/test":"^1.44.0"},"scripts":{"test:e2e":"playwright test"}}""");

        var project = Assert.Single(ProjectDiscoverer.Discover(_root));
        Assert.Equal("javascript", project.Kind);
        Assert.Equal("playwright", project.Framework);
    }

    [Fact]
    public void Ignores_packages_without_a_test_framework_and_node_modules()
    {
        Write("lib/package.json", """{"name":"lib","dependencies":{"vue":"^3.0.0"}}""");
        Write("shop/node_modules/dep/package.json", """{"name":"dep","devDependencies":{"jest":"^29.0.0"},"scripts":{"test":"jest"}}""");

        Assert.Empty(ProjectDiscoverer.Discover(_root));
    }
}

public class HistoryStoreTests : IDisposable
{
    private readonly string _out = Directory.CreateTempSubdirectory("testlens-hist").FullName;
    public void Dispose() => Directory.Delete(_out, true);

    private static RunSnapshot Snapshot(DateTimeOffset ts) => new()
    {
        Timestamp = ts,
        ToolVersion = "test",
        Root = "/repo",
        Projects = { new ProjectResult { Name = "P", Path = "p", Kind = "csharp", Framework = "xunit" } },
    };

    [Fact]
    public void Roundtrips_snapshots_ordered_by_timestamp()
    {
        var store = new HistoryStore(_out);
        store.Save(Snapshot(new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero)));
        store.Save(Snapshot(new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero)));

        var runs = store.LoadAll();

        Assert.Equal(2, runs.Count);
        Assert.True(runs[0].Timestamp < runs[1].Timestamp);
        Assert.Equal("P", runs[0].Projects[0].Name);
    }

    [Fact]
    public void Same_second_runs_get_distinct_ids()
    {
        var store = new HistoryStore(_out);
        var ts = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        store.Save(Snapshot(ts));
        store.Save(Snapshot(ts));

        Assert.Equal(2, store.LoadAll().Select(r => r.Id).Distinct().Count());
    }
}

public class ReportGeneratorTests : IDisposable
{
    private readonly string _out = Directory.CreateTempSubdirectory("testlens-rep").FullName;
    public void Dispose() => Directory.Delete(_out, true);

    [Fact]
    public void Injects_runs_and_version_into_the_template()
    {
        var runs = DemoData.Generate("9.9.9");

        var path = ReportGenerator.Generate(runs, _out, "9.9.9");
        var html = File.ReadAllText(path);

        Assert.Contains("window.TESTLENS_RUNS = [", html);
        Assert.Contains("storefront-web", html);
        Assert.Contains("v9.9.9", html);
        Assert.Contains("Powered by IT Performance ApS", html);
        Assert.DoesNotContain("__TESTLENS_VERSION__", html);
    }

    [Fact]
    public void Demo_data_is_consistent()
    {
        var runs = DemoData.Generate("x");

        Assert.True(runs.Count >= 10);
        foreach (var project in runs.SelectMany(r => r.Projects))
        {
            var e = project.Execution!;
            Assert.True(e.Passed + e.Failed + e.Skipped <= project.Discovery.Total + e.Skipped);
            Assert.True(e.Passed >= 0 && e.Failed >= 0);
        }
    }
}
