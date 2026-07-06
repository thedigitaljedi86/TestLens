using TestLens.Analysis;
using Xunit;

namespace TestLens.Cli.Tests;

public class SourceSplitterTests
{
    [Fact]
    public void Line_comments_go_to_the_comment_stream()
    {
        var (code, comments) = SourceSplitter.Split("var x = 1; // [Test] here\nvar y = 2;", true);
        Assert.DoesNotContain("[Test]", code);
        Assert.Contains("[Test]", comments);
        Assert.Contains("var y = 2;", code);
    }

    [Fact]
    public void Block_comments_go_to_the_comment_stream()
    {
        var (code, comments) = SourceSplitter.Split("before /* [Fact]\n[Fact] */ after", true);
        Assert.DoesNotContain("[Fact]", code);
        Assert.Equal(2, CountOf(comments, "[Fact]"));
        Assert.Contains("after", code);
    }

    [Fact]
    public void Comment_markers_inside_strings_are_not_comments()
    {
        var (code, comments) = SourceSplitter.Split("var url = \"http://example.com\"; var z = 1;", true);
        Assert.Contains("var z = 1;", code);
        Assert.DoesNotContain("example.com", comments);
    }

    [Fact]
    public void Csharp_verbatim_strings_with_escaped_quotes_are_handled()
    {
        var (code, _) = SourceSplitter.Split("var s = @\"a \"\"quoted\"\" // not a comment\"; var tail = 1;", true);
        Assert.Contains("var tail = 1;", code);
    }

    [Fact]
    public void Js_template_literals_are_treated_as_strings()
    {
        var (code, comments) = SourceSplitter.Split("const t = `it('inside template')`; // it('in comment')", false);
        Assert.DoesNotContain("inside template", comments);
        Assert.Contains("in comment", comments);
    }

    private static int CountOf(string text, string needle)
    {
        int count = 0, i = 0;
        while ((i = text.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { count++; i += needle.Length; }
        return count;
    }
}

public class CSharpTestScannerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("testlens-cs").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    private void WriteSource(string name, string content) => File.WriteAllText(Path.Combine(_dir, name), content);

    [Fact]
    public void Counts_xunit_facts_theories_skips_and_commented_out_tests()
    {
        WriteSource("FactTests.cs", """
            public class FactTests
            {
                [Fact]
                public void A() { }

                [Theory]
                [InlineData(1)]
                public void B(int x) { }

                [Fact(Skip = "broken")]
                public void C() { }

                // [Fact]
                // public void Old() { }
            }
            """);

        var counts = CSharpTestScanner.Scan(_dir);

        Assert.Equal(3, counts.Total);
        Assert.Equal(1, counts.Ignored);
        Assert.Equal(1, counts.CommentedOut);
        Assert.Equal(1, counts.Files);
    }

    [Fact]
    public void Counts_nunit_tests_testcases_ignore_and_explicit()
    {
        WriteSource("NUnitTests.cs", """
            public class NUnitTests
            {
                [Test]
                public void A() { }

                [TestCase(1)]
                [TestCase(2)]
                public void B(int x) { }

                [Test]
                [Ignore("later")]
                public void C() { }

                [Test]
                [Explicit]
                public void D() { }
            }
            """);

        var counts = CSharpTestScanner.Scan(_dir);

        Assert.Equal(5, counts.Total); // 3 [Test] + 2 [TestCase]
        Assert.Equal(1, counts.Ignored);
        Assert.Equal(1, counts.Explicit);
    }

    [Fact]
    public void Class_level_explicit_marks_every_test_in_the_fixture()
    {
        WriteSource("SlowSuite.cs", """
            [Explicit("nightly only")]
            [TestFixture]
            public class SlowSuite
            {
                [Test]
                public void A() { }

                [TestCase(1)]
                [TestCase(2)]
                public void B(int x) { }

                [Test]
                public void C() { }
            }
            """);

        var counts = CSharpTestScanner.Scan(_dir);

        Assert.Equal(4, counts.Total);     // 2 [Test] + 2 [TestCase]
        Assert.Equal(4, counts.Explicit);  // all of them, via the class-level [Explicit]
    }

    [Fact]
    public void Only_tests_inside_the_explicit_class_are_counted_as_explicit()
    {
        WriteSource("Mixed.cs", """
            [Explicit]
            public class Nightly
            {
                [Test] public void A() { }
                [Test] public void B() { }
            }

            [TestFixture]
            public class Fast
            {
                [Test] public void C() { }
                [Test] public void D() { }
            }
            """);

        var counts = CSharpTestScanner.Scan(_dir);

        Assert.Equal(4, counts.Total);
        Assert.Equal(2, counts.Explicit); // only A and B, not C and D
    }

    [Fact]
    public void Xunit_fact_and_theory_with_explicit_true_are_counted()
    {
        WriteSource("XunitExplicit.cs", """
            public class XunitExplicit
            {
                [Fact(Explicit = true)]
                public void A() { }

                [Theory(Explicit = true)]
                [InlineData(1)]
                public void B(int x) { }

                [Fact]
                public void C() { }
            }
            """);

        var counts = CSharpTestScanner.Scan(_dir);

        Assert.Equal(3, counts.Total);
        Assert.Equal(2, counts.Explicit); // A and B
    }

    [Fact]
    public void Skips_bin_and_obj_directories()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "obj"));
        File.WriteAllText(Path.Combine(_dir, "obj", "Generated.cs"), "[Fact] public void X() { }");

        Assert.Equal(0, CSharpTestScanner.Scan(_dir).Total);
    }
}

public class JsTestScannerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("testlens-js").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Counts_declarations_skips_focus_and_commented_out_tests()
    {
        File.WriteAllText(Path.Combine(_dir, "app.spec.ts"), """
            describe('suite', () => {
              it('works', () => {});
              test('also works', () => {});
              it.skip('later', () => {});
              xit('parked', () => {});
              it.only('focused', () => {});
              fit('also focused', () => {});
              it.todo('write me');
              // it('commented away', () => {});
            });
            """);

        var counts = JsTestScanner.Scan(_dir);

        Assert.Equal(7, counts.Total);
        Assert.Equal(3, counts.Ignored);   // skip + xit + todo
        Assert.Equal(2, counts.Explicit);  // only + fit
        Assert.Equal(1, counts.CommentedOut);
    }

    [Fact]
    public void Counts_playwright_fixme_as_ignored_and_only_as_explicit()
    {
        File.WriteAllText(Path.Combine(_dir, "checkout.spec.ts"), """
            import { test, expect } from '@playwright/test';

            test.describe('checkout', () => {
              test('adds an item', async ({ page }) => {});
              test.skip('coupon flow', async ({ page }) => {});
              test.fixme('broken on webkit', async ({ page }) => {});
              test.only('focused', async ({ page }) => {});
            });
            """);

        var counts = JsTestScanner.Scan(_dir);

        Assert.Equal(4, counts.Total);
        Assert.Equal(2, counts.Ignored);   // skip + fixme
        Assert.Equal(1, counts.Explicit);  // only
    }

    [Fact]
    public void Only_test_files_are_scanned()
    {
        File.WriteAllText(Path.Combine(_dir, "app.ts"), "it('not a test file', () => {});");
        Assert.Equal(0, JsTestScanner.Scan(_dir).Total);
    }

    [Fact]
    public void Does_not_match_method_names_ending_in_it()
    {
        File.WriteAllText(Path.Combine(_dir, "x.spec.js"), "commit('msg'); submit(form); it('real', () => {});");
        Assert.Equal(1, JsTestScanner.Scan(_dir).Total);
    }
}
