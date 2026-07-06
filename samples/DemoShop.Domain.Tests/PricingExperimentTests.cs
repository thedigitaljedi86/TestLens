using NUnit.Framework;

namespace DemoShop.Domain.Tests;

// The whole fixture is opt-in: [Explicit] at the class level means every test
// below is explicit, even though none of them repeats the attribute. TestLens
// should attribute all of these to "explicit", not just count the attribute once.
[TestFixture]
[Explicit("Hits the live pricing sandbox - run on demand only")]
public class PricingExperimentTests
{
    [Test]
    public void Applies_seasonal_multiplier()
    {
        Assert.That(OrderNumber.IsValid("ORD-2026-000001"), Is.True);
    }

    [TestCase(2025)]
    [TestCase(2026)]
    public void Formats_order_numbers_for_any_year(int year)
    {
        Assert.That(OrderNumber.Format(year, 1), Does.StartWith($"ORD-{year}-"));
    }
}
