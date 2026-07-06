using TestLens.Execution;
using Xunit;

namespace TestLens.Cli.Tests;

public class JsTestRunnerTests
{
    [Fact]
    public void ParsePlaywrightStats_reads_the_stats_block()
    {
        // Shape emitted by the Playwright JSON reporter.
        var json = """
            {
              "config": {},
              "suites": [],
              "errors": [],
              "stats": {
                "startTime": "2026-07-06T10:00:00.000Z",
                "duration": 4210.5,
                "expected": 12,
                "unexpected": 2,
                "flaky": 1,
                "skipped": 3
              }
            }
            """;

        var result = JsTestRunner.ParsePlaywrightStats(json);

        Assert.Equal("completed", result.Status);
        Assert.Equal(13, result.Passed);  // expected + flaky
        Assert.Equal(2, result.Failed);
        Assert.Equal(3, result.Skipped);
    }

    [Fact]
    public void ParsePlaywrightStats_errors_when_stats_missing()
    {
        var result = JsTestRunner.ParsePlaywrightStats("""{"suites":[]}""");
        Assert.Equal("error", result.Status);
    }
}
