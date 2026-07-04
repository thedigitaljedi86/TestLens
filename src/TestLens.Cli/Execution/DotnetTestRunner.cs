using System.Diagnostics;
using System.Xml.Linq;
using TestLens.Models;

namespace TestLens.Execution;

/// <summary>Runs `dotnet test` for a C# test project and parses the TRX result file.</summary>
public static class DotnetTestRunner
{
    public static ExecutionResult Run(string projectDir, TimeSpan timeout)
    {
        var resultsDir = Path.Combine(Path.GetTempPath(), "testlens-trx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(resultsDir);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = ProcessRunner.Run(
                "dotnet",
                $"test --nologo --verbosity quiet --logger trx --results-directory \"{resultsDir}\"",
                projectDir,
                timeout);
            stopwatch.Stop();

            if (result.TimedOut)
                return Error($"Timed out after {timeout.TotalSeconds:0}s", stopwatch);

            var trxFile = Directory.EnumerateFiles(resultsDir, "*.trx").FirstOrDefault();
            if (trxFile is null)
            {
                var detail = Truncate(result.Stderr.Length > 0 ? result.Stderr : result.Stdout, 800);
                return Error($"dotnet test produced no results (exit {result.ExitCode}). {detail}", stopwatch);
            }

            var execution = ParseTrx(trxFile);
            execution.DurationMs = stopwatch.ElapsedMilliseconds;
            return execution;
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
