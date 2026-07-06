using System.Text.RegularExpressions;
using TestLens.Models;

namespace TestLens.Analysis;

/// <summary>
/// Counts test methods in C# sources by their attributes (xUnit, NUnit, MSTest),
/// including ignored/skipped, explicit and commented-out tests.
/// </summary>
/// <remarks>
/// Explicit detection understands the three ways a test opts out of a normal run:
/// a method-level <c>[Explicit]</c> (NUnit), a class-level <c>[Explicit]</c> on
/// the fixture (NUnit - which makes <em>every</em> test in the class explicit),
/// and xUnit's <c>[Fact(Explicit = true)]</c> / <c>[Theory(Explicit = true)]</c>.
/// </remarks>
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

    // xUnit v3: [Fact(Explicit = true)] / [Theory(Explicit = true)].
    [GeneratedRegex(@"Explicit\s*=\s*true", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex ExplicitTrue();

    // A type declaration together with the attribute block(s) that decorate it,
    // e.g. "[Explicit] [TestFixture] public sealed class Foo".
    [GeneratedRegex(
        @"(?<attrs>(?:\[[^\]]*\]\s*)+)(?:(?:public|internal|private|protected|sealed|abstract|static|partial|file|new)\s+)*(?:class|struct|record)\b",
        RegexOptions.Compiled)]
    private static partial Regex TypeDeclarationWithAttrs();

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
            counts.Explicit += CountExplicit(code);
            counts.CommentedOut += TestAttribute().Matches(comments).Count;
        }
        return counts;
    }

    /// <summary>
    /// Counts how many discovered tests are explicit. A test is explicit when its
    /// own attribute group carries <c>[Explicit]</c> or <c>Explicit = true</c>, or
    /// when it lives inside a fixture whose class declaration is <c>[Explicit]</c>.
    /// </summary>
    private static int CountExplicit(string code)
    {
        var explicitClasses = ExplicitClassRanges(code);
        int count = 0;

        foreach (Match test in TestAttribute().Matches(code))
        {
            if (explicitClasses.Any(r => test.Index > r.Open && test.Index < r.Close))
            {
                count++; // covered by a class-level [Explicit] on the enclosing fixture
                continue;
            }

            var group = AttributeGroup(code, test.Index);
            if (ExplicitAttribute().IsMatch(group) || ExplicitTrue().IsMatch(group))
                count++;
        }

        return count;
    }

    /// <summary>Body spans of the classes/records marked <c>[Explicit]</c> at type level.</summary>
    private static List<(int Open, int Close)> ExplicitClassRanges(string code)
    {
        var ranges = new List<(int, int)>();
        foreach (Match m in TypeDeclarationWithAttrs().Matches(code))
        {
            if (!ExplicitAttribute().IsMatch(m.Groups["attrs"].Value)) continue;
            if (BraceRange(code, m.Index + m.Length) is { } body) ranges.Add(body);
        }
        return ranges;
    }

    /// <summary>The full attribute run decorating the same member as the attribute at <paramref name="index"/>.</summary>
    private static string AttributeGroup(string code, int index)
    {
        int start = index;
        while (true)
        {
            int p = start - 1;
            while (p >= 0 && char.IsWhiteSpace(code[p])) p--;
            if (p >= 0 && code[p] == ']') start = MatchOpen(code, p);
            else break;
        }

        int end = MatchClose(code, index);
        while (true)
        {
            int p = end + 1;
            while (p < code.Length && char.IsWhiteSpace(code[p])) p++;
            if (p < code.Length && code[p] == '[') end = MatchClose(code, p);
            else break;
        }

        return code.Substring(start, end - start + 1);
    }

    /// <summary>Span of the brace-delimited body that follows <paramref name="from"/>, or null for a bodyless declaration.</summary>
    private static (int Open, int Close)? BraceRange(string code, int from)
    {
        int i = from;
        while (i < code.Length && code[i] != '{')
        {
            if (code[i] == ';') return null; // e.g. a positional record with no body
            i++;
        }
        if (i >= code.Length) return null;

        int depth = 0;
        for (int j = i; j < code.Length; j++)
        {
            if (code[j] == '{') depth++;
            else if (code[j] == '}' && --depth == 0) return (i, j);
        }
        return (i, code.Length - 1);
    }

    private static int MatchClose(string code, int open)
    {
        int depth = 0;
        for (int i = open; i < code.Length; i++)
        {
            if (code[i] == '[') depth++;
            else if (code[i] == ']' && --depth == 0) return i;
        }
        return code.Length - 1;
    }

    private static int MatchOpen(string code, int close)
    {
        int depth = 0;
        for (int i = close; i >= 0; i--)
        {
            if (code[i] == ']') depth++;
            else if (code[i] == '[' && --depth == 0) return i;
        }
        return 0;
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
