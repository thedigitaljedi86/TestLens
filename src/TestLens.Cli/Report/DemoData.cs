using TestLens.Models;

namespace TestLens.Report;

/// <summary>
/// Generates a realistic-looking demo history so the report can be previewed
/// without scanning a real codebase (`testlens demo`).
/// </summary>
public static class DemoData
{
    private sealed record DemoProject(string Name, string Path, string Kind, string Framework,
        int StartTests, double Growth, double StartFailRate, double FailTrend, int Ignored, int Explicit, int Commented);

    public static List<RunSnapshot> Generate(string toolVersion)
    {
        var projects = new List<DemoProject>
        {
            new("Ordering.Api.Tests",        "services/ordering/tests",   "csharp",  "xunit",         182, 4.0, 0.06, -0.55, 3, 2, 4),
            new("Billing.Domain.Tests",      "services/billing/tests",    "csharp",  "nunit",         240, 2.2, 0.02, -0.30, 6, 4, 2),
            new("Warehouse.Legacy.Tests",    "services/warehouse/tests",  "csharp",  "mstest",         96, 0.3, 0.11,  0.35, 9, 0, 12),
            new("storefront-web",            "apps/storefront",           "vue",     "vitest",        146, 5.5, 0.04, -0.60, 2, 1, 3),
            new("admin-dashboard",           "apps/admin",                "angular", "karma-jasmine", 118, 3.0, 0.07, -0.25, 5, 0, 6),
            new("customer-portal",           "apps/portal",               "angular", "jest",           74, 6.5, 0.09, -0.70, 1, 0, 1),
        };

        var random = new Random(42);
        var runs = new List<RunSnapshot>();
        var start = DateTimeOffset.UtcNow.AddDays(-77);
        const int runCount = 12;

        for (int i = 0; i < runCount; i++)
        {
            double progress = i / (double)(runCount - 1);
            var timestamp = start.AddDays(i * 7).AddMinutes(random.Next(-500, 500));

            var run = new RunSnapshot
            {
                Timestamp = timestamp,
                Label = i == runCount - 1 ? "Latest" : null,
                ToolVersion = toolVersion,
                Root = "/repos/acme-commerce",
                TestsExecuted = true,
            };

            foreach (var p in projects)
            {
                int total = p.StartTests + (int)(p.Growth * i * 3 + random.Next(-3, 4));
                double failRate = Math.Clamp(p.StartFailRate * (1 + p.FailTrend * progress) + (random.NextDouble() - 0.5) * 0.02, 0, 0.5);

                int ignored = Math.Max(0, p.Ignored + random.Next(-1, 2));
                int explicitTests = p.Explicit;
                int skipped = ignored + explicitTests;
                int runnable = Math.Max(0, total - skipped);
                int failed = (int)Math.Round(runnable * failRate);
                int passed = runnable - failed;

                run.Projects.Add(new ProjectResult
                {
                    Name = p.Name,
                    Path = p.Path,
                    Kind = p.Kind,
                    Framework = p.Framework,
                    Discovery = new DiscoveryCounts
                    {
                        Total = total,
                        Ignored = ignored,
                        Explicit = explicitTests,
                        CommentedOut = Math.Max(0, p.Commented + random.Next(-2, 2) - i / 4),
                        Files = Math.Max(1, total / 9),
                    },
                    Execution = new ExecutionResult
                    {
                        Status = "completed",
                        Passed = passed,
                        Failed = failed,
                        Skipped = skipped,
                        DurationMs = 4000 + total * random.Next(35, 60),
                    },
                });
            }

            runs.Add(run);
        }

        return runs;
    }
}
