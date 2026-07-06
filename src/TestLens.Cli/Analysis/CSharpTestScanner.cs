using System.Text.RegularExpressions;
using TestLens.Models;

namespace TestLens.Analysis;

/// <summary>
/// Counts test methods in C# sources by their attributes (xUnit, NUnit, MSTest,
/// and Playwright for .NET, which builds on those). A small structural pass keeps
/// track of the enclosing type so that class-level <c>[Explicit]</c>/<c>[Ignore]</c>
/// mark every test in the class, not just one. Ignored/skipped, explicit and
/// commented-out tests are reported separately.
/// </summary>
public static partial class CSharpTestScanner
{
    // Attribute names (last segment, e.g. NUnit.Framework.Test -> "Test") that declare a test.
    private static readonly HashSet<string> TestAttributeNames = new(StringComparer.Ordinal)
    {
        "Fact", "Theory", "Test", "TestCase", "TestMethod", "DataTestMethod",
    };

    // For counting a commented-out test we reuse a cheap regex over the comment stream.
    [GeneratedRegex(@"\[\s*(Fact|Theory|Test|TestCase|TestMethod|DataTestMethod)\s*[\](,)]", RegexOptions.Compiled)]
    private static partial Regex CommentedTestAttribute();

    [GeneratedRegex(@"\bSkip\s*=", RegexOptions.Compiled)]
    private static partial Regex XunitSkipArg();

    [GeneratedRegex(@"\bExplicit\s*=\s*true", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex XunitExplicitArg();

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

            var before = (counts.Total, counts.CommentedOut);
            ScanCode(code, counts);
            counts.CommentedOut += CommentedTestAttribute().Matches(comments).Count;

            if (counts.Total != before.Total || counts.CommentedOut != before.CommentedOut)
                counts.Files++;
        }
        return counts;
    }

    private readonly record struct ParsedAttribute(string Name, string Args);

    private readonly record struct ClassScope(int BraceDepth, bool Explicit, bool Ignore);

    /// <summary>
    /// Walks the (comment- and string-stripped) code once, attributing each test
    /// method's category from its own attributes OR'd with its enclosing type.
    /// </summary>
    private static void ScanCode(string code, DiscoveryCounts counts)
    {
        int i = 0, depth = 0;
        var classes = new Stack<ClassScope>();
        var attrs = new List<ParsedAttribute>();
        (bool expl, bool ign)? pendingClass = null;

        while (i < code.Length)
        {
            char c = code[i];

            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (c == '[')
            {
                int end = SkipBalanced(code, i);
                ParseAttributes(code.AsSpan(i + 1, end - i - 2), attrs);
                i = end;
                continue;
            }

            if (c == '{')
            {
                depth++;
                if (pendingClass is { } pc)
                {
                    classes.Push(new ClassScope(depth, pc.expl, pc.ign));
                    pendingClass = null;
                }
                i++;
                continue;
            }

            if (c == '}')
            {
                if (classes.Count > 0 && classes.Peek().BraceDepth == depth) classes.Pop();
                if (depth > 0) depth--;
                i++;
                continue;
            }

            if (c == '(')
            {
                CommitDeclaration(attrs, classes, counts);
                attrs.Clear();
                i = SkipBalanced(code, i);
                continue;
            }

            if (c == ';')
            {
                // End of a statement, field, or a bodyless type (e.g. `record R(...);`).
                pendingClass = null;
                attrs.Clear();
                i++;
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < code.Length && (char.IsLetterOrDigit(code[i]) || code[i] == '_')) i++;
                var word = code.AsSpan(start, i - start);

                if (word is "class" or "struct" or "record")
                {
                    pendingClass = (AnyExplicit(attrs), AnyIgnore(attrs));
                    attrs.Clear();
                }
                else if (word is "interface" or "enum" or "namespace" or "delegate")
                {
                    // Not a test-bearing type scope; drop any attributes that preceded it.
                    attrs.Clear();
                }
                // Otherwise a modifier / return type / identifier: keep accumulating attributes.
                continue;
            }

            i++;
        }
    }

    private static void CommitDeclaration(List<ParsedAttribute> attrs, Stack<ClassScope> classes, DiscoveryCounts counts)
    {
        int testCount = 0;
        foreach (var a in attrs)
            if (TestAttributeNames.Contains(a.Name)) testCount++;
        if (testCount == 0) return;

        bool ignore = AnyIgnore(attrs);
        bool expl = AnyExplicit(attrs);
        if (classes.Count > 0)
        {
            ignore |= classes.Peek().Ignore;
            expl |= classes.Peek().Explicit;
        }

        counts.Total += testCount;
        if (ignore) counts.Ignored += testCount;
        else if (expl) counts.Explicit += testCount;
    }

    private static bool AnyIgnore(List<ParsedAttribute> attrs)
    {
        foreach (var a in attrs)
        {
            if (a.Name == "Ignore") return true;
            // xUnit: [Fact(Skip = "...")] / [Theory(Skip = "...")]
            if ((a.Name == "Fact" || a.Name == "Theory") && XunitSkipArg().IsMatch(a.Args)) return true;
        }
        return false;
    }

    private static bool AnyExplicit(List<ParsedAttribute> attrs)
    {
        foreach (var a in attrs)
        {
            if (a.Name == "Explicit") return true;
            // xUnit v3: [Fact(Explicit = true)] / [Theory(Explicit = true)]
            if ((a.Name == "Fact" || a.Name == "Theory") && XunitExplicitArg().IsMatch(a.Args)) return true;
        }
        return false;
    }

    /// <summary>Splits the inside of an attribute list into individual attributes.</summary>
    private static void ParseAttributes(ReadOnlySpan<char> inner, List<ParsedAttribute> into)
    {
        int i = 0;
        while (i < inner.Length)
        {
            // Skip separators/whitespace between attributes.
            while (i < inner.Length && (char.IsWhiteSpace(inner[i]) || inner[i] == ',')) i++;
            if (i >= inner.Length) break;

            // Attribute target prefix (assembly:, return:, ...) - skip the whole attribute.
            int nameStart = i;
            while (i < inner.Length && (char.IsLetterOrDigit(inner[i]) || inner[i] == '_' || inner[i] == '.')) i++;
            var qualified = inner.Slice(nameStart, i - nameStart);

            // Grab optional (args), balancing parentheses.
            string args = "";
            while (i < inner.Length && char.IsWhiteSpace(inner[i])) i++;
            if (i < inner.Length && inner[i] == '(')
            {
                int argStart = i + 1;
                int parenDepth = 0;
                while (i < inner.Length)
                {
                    if (inner[i] == '(') parenDepth++;
                    else if (inner[i] == ')') { parenDepth--; if (parenDepth == 0) break; }
                    i++;
                }
                args = inner.Slice(argStart, Math.Max(0, i - argStart)).ToString();
                if (i < inner.Length) i++; // consume ')'
            }

            if (qualified.Length > 0 && !qualified.EndsWith(":", StringComparison.Ordinal))
            {
                // Attribute targets end up captured with a trailing ':' handled above; use last name segment.
                int dot = qualified.LastIndexOf('.');
                var name = dot >= 0 ? qualified.Slice(dot + 1) : qualified;
                into.Add(new ParsedAttribute(name.ToString(), args));
            }

            // Advance to the next top-level comma.
            while (i < inner.Length && inner[i] != ',') i++;
        }
    }

    /// <summary>
    /// Returns the index just past the balanced group that starts at <paramref name="start"/>
    /// (which must be '[' or '('), tracking nested [], (), {} so array/object args don't
    /// close the group early. Strings/chars are already emptied by the source splitter.
    /// </summary>
    private static int SkipBalanced(string code, int start)
    {
        var stack = new Stack<char>();
        int i = start;
        while (i < code.Length)
        {
            char c = code[i];
            if (c is '[' or '(' or '{') stack.Push(c);
            else if (c is ']' or ')' or '}')
            {
                if (stack.Count > 0) stack.Pop();
                if (stack.Count == 0) return i + 1;
            }
            i++;
        }
        return code.Length;
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
