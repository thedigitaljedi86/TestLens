using NUnit.Framework;

namespace DemoShop.Domain.Tests;

public static class OrderNumber
{
    public static string Format(int year, int sequence) => $"ORD-{year}-{sequence:D6}";
    public static bool IsValid(string value) =>
        System.Text.RegularExpressions.Regex.IsMatch(value, @"^ORD-\d{4}-\d{6}$");
}

[TestFixture]
public class OrderNumberTests
{
    [Test]
    public void Format_pads_sequence_to_six_digits()
    {
        Assert.That(OrderNumber.Format(2026, 42), Is.EqualTo("ORD-2026-000042"));
    }

    [TestCase("ORD-2026-000001", true)]
    [TestCase("ORD-26-000001", false)]
    [TestCase("BAD-2026-000001", false)]
    public void IsValid_checks_the_full_pattern(string candidate, bool expected)
    {
        Assert.That(OrderNumber.IsValid(candidate), Is.EqualTo(expected));
    }

    [Test]
    [Ignore("Sequence reset rules are being redesigned in DOM-311")]
    public void Sequence_resets_every_year()
    {
        Assert.Fail("not implemented");
    }

    [Test]
    [Explicit("Slow - generates one million order numbers")]
    public void Format_handles_the_full_sequence_range()
    {
        for (var i = 1; i <= 1_000_000; i += 100_000)
            Assert.That(OrderNumber.IsValid(OrderNumber.Format(2026, i)), Is.True);
    }

    /*
    [Test]
    public void Legacy_format_supported_until_v2()
    {
        Assert.That(OrderNumber.IsValid("ORD-99-1"), Is.True);
    }
    */
}

// Class-level [Explicit]: every test in this fixture is explicit and only runs
// when selected by name - TestLens counts all three, not just one attribute.
[TestFixture]
[Explicit("Load suite - only run on demand")]
public class OrderNumberLoadTests
{
    [Test]
    public void Formats_a_large_batch()
    {
        for (var i = 0; i < 250_000; i++)
            Assert.That(OrderNumber.Format(2026, i), Does.StartWith("ORD-2026-"));
    }

    [TestCase(2020)]
    [TestCase(2026)]
    public void Validates_every_year(int year)
    {
        Assert.That(OrderNumber.IsValid(OrderNumber.Format(year, 1)), Is.True);
    }
}
