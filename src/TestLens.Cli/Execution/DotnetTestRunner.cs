using System.Diagnostics;
using System.Xml.Linq;
using TestLens.Models;

namespace TestLens.Execution;

/// <summary>Runs `dotnet test` for a C# test project and parses the TRX result file.</summary>
public static class DotnetTestRunner
{
    /// <summary>
    /// Runs the suite. With <paramref name="includeExplicit"/> explicit tests are run too:
    /// xUnit v3 through its <c>xUnit.Explicit</c> setting, NUnit through a second pass that
    /// selects only the explicit tests (NUnit skips them whenever the filter also matches
    /// a regular test).
    /// </summary>
    public static ExecutionResult Run(string projectDir, TimeSpan timeout, bool includeExplicit = false,
        IReadOnlyCollection<string>? nunitExplicitSelectors = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var execution = RunOnce(projectDir, includeExplicit ? "-- xUnit.Explicit=on" : "", timeout, stopwatch);

        if (execution.Status == "completed" && includeExplicit && nunitExplicitSelectors is { Count: > 0 })
        {
            var where = string.Join(" || ", nunitExplicitSelectors);
            var explicitRun = RunOnce(projectDir, $"--no-build -- \"NUnit.Where={where}\"", timeout, stopwatch);
            if (explicitRun.Status == "completed")
            {
                // The first TRX lists the explicit tests as not executed (skipped); move them over.
                int executed = explicitRun.Passed + explicitRun.Failed;
                execution.Passed += explicitRun.Passed;
                execution.Failed += explicitRun.Failed;
                execution.Skipped = Math.Max(0, execution.Skipped - executed);
            }
            else
            {
                execution.Error = $"Explicit tests could not be run: {explicitRun.Error}";
            }
        }

        execution.DurationMs = stopwatch.ElapsedMilliseconds;
        return execution;
    }

    private static ExecutionResult RunOnce(string projectDir, string extraArgs, TimeSpan timeout, Stopwatch stopwatch)
    {
        var resultsDir = Path.Combine(Path.GetTempPath(), "testlens-trx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(resultsDir);

        try
        {
            var result = ProcessRunner.Run(
                "dotnet",
                $"test --nologo --verbosity quiet --logger trx --results-directory \"{resultsDir}\" {extraArgs}".TrimEnd(),
                projectDir,
                timeout);

            if (result.TimedOut)
                return Error($"Timed out after {timeout.TotalSeconds:0}s", stopwatch);

            var trxFile = Directory.EnumerateFiles(resultsDir, "*.trx").FirstOrDefault();
            if (trxFile is null)
            {
                var detail = Truncate(result.Stderr.Length > 0 ? result.Stderr : result.Stdout, 800);
                return Error($"dotnet test produced no results (exit {result.ExitCode}). {detail}", stopwatch);
            }

            return ParseTrx(trxFile);
        }
        finally
        {
            try { Directory.Delete(resultsDir, recursive: true); } catch (IOException) { }
        }
    }

    private static ExecutionResult ParseTrx(string trxFile)
    {
        var doc = XDocument.Load(trxFile);
        XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        var counters = doc.Descendants(ns + "Counters").FirstOrDefault();

        int Attr(string name) =>
            int.TryParse(counters?.Attribute(name)?.Value, out var v) ? v : 0;

        int total = Attr("total");
        int passed = Attr("passed");
        int failed = Attr("failed") + Attr("error") + Attr("timeout") + Attr("aborted");
        int skipped = Math.Max(0, total - passed - failed);

        return new ExecutionResult { Status = "completed", Passed = passed, Failed = failed, Skipped = skipped };
    }

    private static ExecutionResult Error(string message, Stopwatch stopwatch) =>
        new() { Status = "error", Error = message, DurationMs = stopwatch.ElapsedMilliseconds };

    internal static string Truncate(string text, int max)
    {
        text = text.Trim();
        return text.Length <= max ? text : text[..max] + "…";
    }
}
