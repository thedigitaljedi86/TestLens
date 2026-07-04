using Xunit;

namespace DemoShop.Api.Tests;

public class Cart
{
    private readonly Dictionary<string, int> _items = new();

    public void Add(string sku, int quantity = 1)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        _items[sku] = _items.GetValueOrDefault(sku) + quantity;
    }

    public int Count => _items.Values.Sum();
    public bool Contains(string sku) => _items.ContainsKey(sku);
}

public class CartTests
{
    [Fact]
    public void Add_single_item_increases_count()
    {
        var cart = new Cart();
        cart.Add("apple");
        Assert.Equal(1, cart.Count);
    }

    [Fact]
    public void Add_same_sku_twice_accumulates_quantity()
    {
        var cart = new Cart();
        cart.Add("apple", 2);
        cart.Add("apple", 3);
        Assert.Equal(5, cart.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Add_rejects_non_positive_quantity(int quantity)
    {
        var cart = new Cart();
        Assert.Throws<ArgumentOutOfRangeException>(() => cart.Add("apple", quantity));
    }

    [Fact]
    public void Contains_reports_added_sku()
    {
        var cart = new Cart();
        cart.Add("banana");
        Assert.True(cart.Contains("banana"));
    }

    [Fact]
    public void Fails_on_purpose_to_demo_the_report()
    {
        // TestLens should show this project as "Failing".
        Assert.Equal(3, 1 + 1);
    }

    [Fact(Skip = "Flaky on CI - waiting for the pricing service stub")]
    public void Checkout_applies_discount_codes()
    {
        Assert.True(true);
    }

    // [Fact]
    // public void Old_tax_rules_apply_before_2020()
    // {
    //     Assert.Equal(0.25m, TaxRules.RateFor(2019));
    // }
}
