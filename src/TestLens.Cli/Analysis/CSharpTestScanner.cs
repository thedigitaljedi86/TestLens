using System.Text.RegularExpressions;
using TestLens.Models;

namespace TestLens.Analysis;

/// <summary>
/// Counts test methods in C# sources by their attributes (xUnit, NUnit, MSTest),
/// including ignored/skipped, [Explicit] and commented-out tests.
/// </summary>
public static partial class CSharpTestScanner
{
    [GeneratedRegex(@"\[\s*(Fact|Theory|Test|TestCase|TestMethod|DataTestMethod)\s*[\](,)]", RegexOptions.Compiled)]
    private static partial Regex TestAttribute();

    // [Fact(Skip = "...")] / [Theory(Skip = "...")] / [TestMethod(... ) with Ignore] etc.
    [GeneratedRegex(@"\[\s*(Fact|Theory)\s*\([^)]*Skip\s*=", RegexOptions.Compiled)]
    private static partial Regex XunitSkip();

    [GeneratedRegex(@"\[\s*Ignore\s*[\](]", RegexOptions.Compiled)]
    private static partial Regex IgnoreAttribute();

    [GeneratedRegex(@"\[\s*Explicit\s*[\](]", RegexOptions.Compiled)]
    private static partial Regex ExplicitAttribute();

    private static readonly string[] SkippedDirs = { "bin", "obj", ".git" };

    public static DiscoveryCounts Scan(string projectDir)
    {
        var counts = new DiscoveryCounts();
        foreach (var file in EnumerateSources(projectDir))
        {
            string source;
            try { source = File.ReadAllText(file); }
            catch (IOException) { continue; }

            var (code, comments) = SourceSplitter.Split(source, csharpVerbatimStrings: true);

            int tests = TestAttribute().Matches(code).Count;
            if (tests == 0 && !TestAttribute().IsMatch(comments)) continue;

            counts.Files++;
            counts.Total += tests;
            counts.Ignored += XunitSkip().Matches(code).Count + IgnoreAttribute().Matches(code).Count;
            counts.Explicit += ExplicitAttribute().Matches(code).Count;
            counts.CommentedOut += TestAttribute().Matches(comments).Count;
        }
        return counts;
    }

    private static IEnumerable<string> EnumerateSources(string projectDir)
    {
        var stack = new Stack<string>();
        stack.Push(projectDir);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(dir); }
            catch (UnauthorizedAccessException) { continue; }

            foreach (var entry in entries)
            {
                if (Directory.Exists(entry))
                {
                    var name = Path.GetFileName(entry);
                    if (!SkippedDirs.Contains(name, StringComparer.OrdinalIgnoreCase) && !name.StartsWith('.'))
                        stack.Push(entry);
                }
                else if (entry.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    yield return entry;
                }
            }
        }
    }
}
