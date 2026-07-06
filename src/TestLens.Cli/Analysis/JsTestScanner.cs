using System.Text.RegularExpressions;
using TestLens.Models;

namespace TestLens.Analysis;

/// <summary>
/// Counts test declarations in JavaScript/TypeScript sources (Jest, Vitest,
/// Jasmine/Karma, Playwright): it/test blocks, skipped (xit, .skip, Playwright's
/// .fixme), focused (.only / fit, reported as "explicit") and commented-out tests.
/// </summary>
public static partial class JsTestScanner
{
    // it( / test( / fit( / xit( / it.skip( / test.only( / test.fixme( / it.each(...)( etc.
    [GeneratedRegex(@"(?<![\w.$])(?:(?<skip>xit|xtest)|(?<focus>fit|ftest)|(?<name>it|test))(?:\.(?<mod>skip|only|each|todo|failing|fixme|concurrent|sequential))?\s*[(`]", RegexOptions.Compiled)]
    private static partial Regex TestDeclaration();

    private static readonly string[] SkippedDirs =
        { "node_modules", "dist", "build", "coverage", ".git", ".angular", "out" };

    private static readonly string[] TestFileSuffixes =
        { ".spec.ts", ".spec.js", ".spec.tsx", ".spec.jsx", ".test.ts", ".test.js", ".test.tsx", ".test.jsx", ".spec.mjs", ".test.mjs" };

    public static DiscoveryCounts Scan(string projectDir)
    {
        var counts = new DiscoveryCounts();
        foreach (var file in EnumerateSources(projectDir))
        {
            string source;
            try { source = File.ReadAllText(file); }
            catch (IOException) { continue; }

            var (code, comments) = SourceSplitter.Split(source, csharpVerbatimStrings: false);

            var matches = TestDeclaration().Matches(code);
            if (matches.Count == 0 && !TestDeclaration().IsMatch(comments)) continue;

            counts.Files++;
            foreach (Match m in matches)
            {
                string mod = m.Groups["mod"].Value;
                counts.Total++;
                if (m.Groups["skip"].Success || mod is "skip" or "todo" or "fixme") counts.Ignored++;
                else if (m.Groups["focus"].Success || mod == "only") counts.Explicit++;
            }
            counts.CommentedOut += TestDeclaration().Matches(comments).Count;
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
                else if (TestFileSuffixes.Any(s => entry.EndsWith(s, StringComparison.OrdinalIgnoreCase)))
                {
                    yield return entry;
                }
            }
        }
    }
}
