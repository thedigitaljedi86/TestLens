using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using TestLens.Models;

namespace TestLens.Execution;

/// <summary>
/// Runs JavaScript/TypeScript test suites (Vitest, Jest, Karma/Jasmine) and
/// parses their machine-readable output where available.
/// </summary>
public static partial class JsTestRunner
{
    private static string Npx => OperatingSystem.IsWindows() ? "npx.cmd" : "npx";
    private static string Npm => OperatingSystem.IsWindows() ? "npm.cmd" : "npm";

    public static ExecutionResult Run(string projectDir, string framework, TimeSpan timeout)
    {
        if (!Directory.Exists(Path.Combine(projectDir, "node_modules")))
        {
            return new ExecutionResult
            {
                Status = "error",
                Error = "node_modules is missing - run 'npm install' (or pass --npm-install) so the test suite can be executed.",
            };
        }

        return framework switch
        {
            "vitest" => RunJsonReporter(projectDir, "vitest run --reporter=json --outputFile=\"{0}\"", timeout),
            "jest" => RunJsonReporter(projectDir, "jest --json --outputFile=\"{0}\" --ci", timeout),
            "playwright" => RunPlaywright(projectDir, timeout),
            "karma-jasmine" => RunKarma(projectDir, timeout),
            _ => RunNpmTest(projectDir, timeout),
        };
    }

    /// <summary>
    /// Runs a Playwright suite (`playwright test`) with the JSON reporter and reads the
    /// run-level <c>stats</c> block. Flaky tests (green after a retry) count as passed.
    /// </summary>
    private static ExecutionResult RunPlaywright(string projectDir, TimeSpan timeout)
    {
        var outputFile = Path.Combine(Path.GetTempPath(), $"testlens-pw-{Guid.NewGuid():N}.json");
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var env = new Dictionary<string, string> { ["PLAYWRIGHT_JSON_OUTPUT_NAME"] = outputFile };
            var result = ProcessRunner.Run(Npx, "playwright test --reporter=json", projectDir, timeout, env);
            stopwatch.Stop();

            if (result.TimedOut)
                return Error($"Timed out after {timeout.TotalSeconds:0}s", stopwatch);

            if (!File.Exists(outputFile))
            {
                var detail = DotnetTestRunner.Truncate(result.Stderr.Length > 0 ? result.Stderr : result.Stdout, 800);
                return Error($"Playwright produced no JSON output (exit {result.ExitCode}). " +
                             $"Browsers may be missing - try 'npx playwright install'. {detail}", stopwatch);
            }

            var execution = ParsePlaywrightStats(File.ReadAllText(outputFile));
            execution.DurationMs = stopwatch.ElapsedMilliseconds;
            return execution;
        }
        catch (JsonException e)
        {
            return Error($"Could not parse Playwright JSON output: {e.Message}", stopwatch);
        }
        finally
        {
            try { File.Delete(outputFile); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Reads the run-level <c>stats</c> block of a Playwright JSON report
    /// (<c>expected</c>=passed, <c>unexpected</c>=failed, <c>flaky</c> counted as passed).
    /// </summary>
    public static ExecutionResult ParsePlaywrightStats(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("stats", out var stats))
            return new ExecutionResult { Status = "error", Error = "Playwright JSON output had no 'stats' block." };

        int Get(string prop) => stats.TryGetProperty(prop, out var v) && v.TryGetInt32(out var n) ? n : 0;

        return new ExecutionResult
        {
            Status = "completed",
            Passed = Get("expected") + Get("flaky"),
            Failed = Get("unexpected"),
            Skipped = Get("skipped"),
        };
    }

    /// <summary>Vitest and Jest share the same JSON result shape.</summary>
    private static ExecutionResult RunJsonReporter(string projectDir, string argsTemplate, TimeSpan timeout)
    {
        var outputFile = Path.Combine(Path.GetTempPath(), $"testlens-{Guid.NewGuid():N}.json");
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = ProcessRunner.Run(Npx, string.Format(argsTemplate, outputFile), projectDir, timeout);
            stopwatch.Stop();

            if (result.TimedOut)
                return Error($"Timed out after {timeout.TotalSeconds:0}s", stopwatch);

            if (!File.Exists(outputFile))
            {
                var detail = DotnetTestRunner.Truncate(result.Stderr.Length > 0 ? result.Stderr : result.Stdout, 800);
                return Error($"Test runner produced no JSON output (exit {result.ExitCode}). {detail}", stopwatch);
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(outputFile));
            var root = doc.RootElement;
            int Get(string prop) => root.TryGetProperty(prop, out var v) && v.TryGetInt32(out var n) ? n : 0;

            return new ExecutionResult
            {
                Status = "completed",
                Passed = Get("numPassedTests"),
                Failed = Get("numFailedTests"),
                Skipped = Get("numPendingTests") + Get("numTodoTests"),
                DurationMs = stopwatch.ElapsedMilliseconds,
            };
        }
        catch (JsonException e)
        {
            return Error($"Could not parse test runner JSON output: {e.Message}", stopwatch);
        }
        finally
        {
            try { File.Delete(outputFile); } catch (IOException) { }
        }
    }

    // "Executed 12 of 14 (2 FAILED) (skipped 2) (0.42 secs / 0.31 secs)"
    [GeneratedRegex(@"Executed\s+(?<executed>\d+)\s+of\s+(?<total>\d+)(?:\s+\((?<failed>\d+)\s+FAILED\))?(?:\s+\(skipped\s+(?<skipped>\d+)\))?", RegexOptions.IgnoreCase)]
    private static partial Regex KarmaSummary();

    // "TOTAL: 2 FAILED, 10 SUCCESS"
    [GeneratedRegex(@"TOTAL:\s*(?:(?<failed>\d+)\s+FAILED,\s*)?(?<success>\d+)\s+SUCCESS", RegexOptions.IgnoreCase)]
    private static partial Regex KarmaTotal();

    private static ExecutionResult RunKarma(string projectDir, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        var env = new Dictionary<string, string>();
        var chrome = FindChrome();
        if (chrome is not null) env["CHROME_BIN"] = chrome;

        var result = ProcessRunner.Run(
            Npx, "ng test --watch=false --progress=false --browsers=ChromeHeadless",
            projectDir, timeout, env);
        stopwatch.Stop();

        if (result.TimedOut)
            return Error($"Timed out after {timeout.TotalSeconds:0}s", stopwatch);

        var output = result.Stdout + "\n" + result.Stderr;

        // Prefer the final "Executed X of Y" line - karma prints one per spec, the last is the summary.
        var summaries = KarmaSummary().Matches(output);
        if (summaries.Count > 0)
        {
            var m = summaries[^1];
            int total = int.Parse(m.Groups["total"].Value);
            int failed = m.Groups["failed"].Success ? int.Parse(m.Groups["failed"].Value) : 0;
            int skipped = m.Groups["skipped"].Success ? int.Parse(m.Groups["skipped"].Value) : 0;
            return new ExecutionResult
            {
                Status = "completed",
                Passed = Math.Max(0, total - failed - skipped),
                Failed = failed,
                Skipped = skipped,
                DurationMs = stopwatch.ElapsedMilliseconds,
            };
        }

        var totalMatch = KarmaTotal().Matches(output);
        if (totalMatch.Count > 0)
        {
            var m = totalMatch[^1];
            return new ExecutionResult
            {
                Status = "completed",
                Passed = int.Parse(m.Groups["success"].Value),
                Failed = m.Groups["failed"].Success ? int.Parse(m.Groups["failed"].Value) : 0,
                DurationMs = stopwatch.ElapsedMilliseconds,
            };
        }

        return Error($"Could not parse karma output (exit {result.ExitCode}). {DotnetTestRunner.Truncate(output, 800)}", stopwatch);
    }

    // Jest-style text summary: "Tests:       1 failed, 2 skipped, 40 passed, 43 total"
    [GeneratedRegex(@"Tests:\s*(?:(?<failed>\d+)\s+failed[,\s]*)?(?:(?<skipped>\d+)\s+(?:skipped|pending|todo)[,\s]*)*(?:(?<passed>\d+)\s+passed[,\s]*)?(?<total>\d+)\s+total", RegexOptions.IgnoreCase)]
    private static partial Regex JestTextSummary();

    private static ExecutionResult RunNpmTest(string projectDir, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = ProcessRunner.Run(Npm, "test --silent", projectDir, timeout);
        stopwatch.Stop();

        if (result.TimedOut)
            return Error($"Timed out after {timeout.TotalSeconds:0}s", stopwatch);

        var output = result.Stdout + "\n" + result.Stderr;
        var m = JestTextSummary().Match(output);
        if (m.Success)
        {
            int Get(string g) => m.Groups[g].Success ? int.Parse(m.Groups[g].Value) : 0;
            return new ExecutionResult
            {
                Status = "completed",
                Passed = Get("passed"),
                Failed = Get("failed"),
                Skipped = Get("skipped"),
                DurationMs = stopwatch.ElapsedMilliseconds,
            };
        }

        return Error($"Could not parse 'npm test' output (exit {result.ExitCode}). {DotnetTestRunner.Truncate(output, 800)}", stopwatch);
    }

    private static string? FindChrome()
    {
        string[] candidates =
        {
            Environment.GetEnvironmentVariable("CHROME_BIN") ?? "",
            "/opt/pw-browsers/chromium",
            "/usr/bin/chromium",
            "/usr/bin/chromium-browser",
            "/usr/bin/google-chrome",
        };
        return candidates.FirstOrDefault(c => c.Length > 0 && File.Exists(c));
    }

    private static ExecutionResult Error(string message, Stopwatch stopwatch) =>
        new() { Status = "error", Error = message, DurationMs = stopwatch.ElapsedMilliseconds };
}
