using eShop.WebAppComponents.Services;

namespace eShop.Application.UnitTests;

[TestClass]
public sealed class ProductBudgetTests
{
    public static IEnumerable<(string input, decimal expected)> ValidBudgets =>
    [
        ("100", 100m),
        ("100.00", 100m),
        ("100.50", 100.50m),
        (" 100.50 ", 100.50m),
        ("0", 0m),
        ("0.00", 0m),
        (".50", 0.50m),
        ("100.", 100m),
        ("000100.05", 100.05m),
        ("792281625142643375935439503.35", 792281625142643375935439503.35m),
        ("79228162514264337593543950335", decimal.MaxValue)
    ];

    [TestMethod]
    [DynamicData(nameof(ValidBudgets))]
    public void SetInput_ValidAmount_UsesExactDecimalValue(string input, decimal expected)
    {
        var budget = new ProductBudget();

        budget.SetInput(input);

        Assert.AreEqual(expected, budget.Amount);
        Assert.IsNull(budget.ValidationMessage);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow(" ")]
    public void SetInput_EmptyValue_RemovesBudgetWithoutValidationError(string? input)
    {
        var budget = new ProductBudget();
        budget.SetInput("100");

        budget.SetInput(input);

        Assert.IsNull(budget.Amount);
        Assert.IsNull(budget.ValidationMessage);
        Assert.IsNull(budget.Assess(50m));
    }

    [TestMethod]
    [DataRow("-1")]
    [DataRow("-0")]
    [DataRow("abc")]
    [DataRow("NaN")]
    [DataRow("Infinity")]
    [DataRow("1e2")]
    [DataRow("100,00")]
    [DataRow("1,000")]
    [DataRow("$100")]
    [DataRow("+")]
    [DataRow(".")]
    [DataRow("100..00")]
    [DataRow("1 00")]
    [DataRow("79228162514264337593543950336")]
    public void SetInput_InvalidAmount_ClearsPriorAffordabilityAndExplainsError(string input)
    {
        var budget = new ProductBudget();
        budget.SetInput("100");
        Assert.IsNotNull(budget.Assess(50m));

        budget.SetInput(input);

        Assert.IsNull(budget.Amount);
        Assert.IsNotNull(budget.ValidationMessage);
        Assert.IsNull(budget.Assess(50m));
    }

    [TestMethod]
    [DataRow("100.001")]
    [DataRow("0.000")]
    [DataRow(".005")]
    public void SetInput_TooManyDecimalPlaces_RejectsRatherThanRounding(string input)
    {
        var budget = new ProductBudget();

        budget.SetInput(input);

        Assert.IsNull(budget.Amount);
        Assert.AreEqual("Use no more than two decimal places for your budget.", budget.ValidationMessage);
    }

    [TestMethod]
    public void SetInput_LargeFractionThatDecimalWouldRound_RejectsInexactBudget()
    {
        var budget = new ProductBudget();

        budget.SetInput("7922816251426433759354395033.51");

        Assert.IsNull(budget.Amount);
        Assert.AreEqual("This budget cannot be represented exactly. Please enter a smaller amount.", budget.ValidationMessage);
    }

    public static IEnumerable<(decimal price, BudgetStatus status, decimal difference, string display)> PriceCases =>
    [
        (75m, BudgetStatus.WithinBudget, 25m, "$25.00 under budget"),
        (100m, BudgetStatus.WithinBudget, 0m, "$0.00 (exactly at budget)"),
        (125m, BudgetStatus.OverBudget, -25m, "$25.00 over budget"),
        (99.99m, BudgetStatus.WithinBudget, 0.01m, "$0.01 under budget"),
        (100.01m, BudgetStatus.OverBudget, -0.01m, "$0.01 over budget"),
        (0m, BudgetStatus.WithinBudget, 100m, "$100.00 under budget")
    ];

    [TestMethod]
    [DynamicData(nameof(PriceCases))]
    public void Assess_PerProductBudget_IncludesEqualityAndShowsExactDifference(decimal price, BudgetStatus status, decimal difference, string display)
    {
        var budget = new ProductBudget();
        budget.SetInput("100");

        var result = budget.Assess(price);

        Assert.IsNotNull(result);
        Assert.AreEqual(status, result.Status);
        Assert.AreEqual(difference, result.Difference);
        Assert.AreEqual(display, result.DifferenceDisplay);
        Assert.AreEqual(status == BudgetStatus.WithinBudget ? "Within budget" : "Over budget", result.StatusDisplay);
    }

    [TestMethod]
    public void Assess_ZeroBudgetAndZeroPrice_IsWithinBudget()
    {
        var budget = new ProductBudget();
        budget.SetInput("0");

        var result = budget.Assess(0m);

        Assert.IsNotNull(result);
        Assert.AreEqual(BudgetStatus.WithinBudget, result.Status);
        Assert.AreEqual(0m, result.Difference);
        Assert.AreEqual("$0.00 (exactly at budget)", result.DifferenceDisplay);
    }

    [TestMethod]
    public void Assess_ZeroBudgetAndPositivePrice_IsOverBudget()
    {
        var budget = new ProductBudget();
        budget.SetInput("0");

        var result = budget.Assess(0.01m);

        Assert.IsNotNull(result);
        Assert.AreEqual(BudgetStatus.OverBudget, result.Status);
        Assert.AreEqual(-0.01m, result.Difference);
        Assert.AreEqual("$0.01 over budget", result.DifferenceDisplay);
    }

    [TestMethod]
    public void SetInput_BudgetChanges_RecalculatesFromOverToUnderWithoutChangingPrice()
    {
        var budget = new ProductBudget();
        budget.SetInput("100");
        var before = budget.Assess(125m);

        budget.SetInput("150");
        var after = budget.Assess(125m);

        Assert.IsNotNull(before);
        Assert.AreEqual(BudgetStatus.OverBudget, before.Status);
        Assert.AreEqual(-25m, before.Difference);
        Assert.IsNotNull(after);
        Assert.AreEqual(BudgetStatus.WithinBudget, after.Status);
        Assert.AreEqual(25m, after.Difference);
    }

    [TestMethod]
    public void SetInput_InvalidThenCorrected_ClearsErrorAndRestoresBudgetResults()
    {
        var budget = new ProductBudget();
        budget.SetInput("invalid");
        Assert.IsNotNull(budget.ValidationMessage);

        budget.SetInput("100.50");

        Assert.IsNull(budget.ValidationMessage);
        var result = budget.Assess(100m);
        Assert.IsNotNull(result);
        Assert.AreEqual(BudgetStatus.WithinBudget, result.Status);
        Assert.AreEqual("$0.50 under budget", result.DifferenceDisplay);
    }

    [TestMethod]
    public void Assess_MissingPrice_DoesNotClassifyProductAsAffordable()
    {
        var budget = new ProductBudget();
        budget.SetInput("100");

        var result = budget.Assess(null);

        Assert.IsNotNull(result);
        Assert.AreEqual(BudgetStatus.PriceUnavailable, result.Status);
        Assert.IsNull(result.Difference);
        Assert.AreEqual("Price unavailable", result.StatusDisplay);
        Assert.AreEqual("A valid product price is needed to check its budget.", result.DifferenceDisplay);
    }

    [TestMethod]
    public void Assess_NegativeCatalogPrice_DoesNotProduceMisleadingAffordability()
    {
        var budget = new ProductBudget();
        budget.SetInput("100");

        var result = budget.Assess(-1m);

        Assert.IsNotNull(result);
        Assert.AreEqual(BudgetStatus.PriceUnavailable, result.Status);
        Assert.IsNull(result.Difference);
    }

    [TestMethod]
    public void Assess_MultipleProducts_DoesNotTreatBudgetAsACombinedTotal()
    {
        var budget = new ProductBudget();
        budget.SetInput("100");

        var first = budget.Assess(75m);
        var second = budget.Assess(80m);
        var third = budget.Assess(125m);

        Assert.IsNotNull(first);
        Assert.AreEqual(BudgetStatus.WithinBudget, first.Status);
        Assert.AreEqual(25m, first.Difference);
        Assert.IsNotNull(second);
        Assert.AreEqual(BudgetStatus.WithinBudget, second.Status);
        Assert.AreEqual(20m, second.Difference);
        Assert.IsNotNull(third);
        Assert.AreEqual(BudgetStatus.OverBudget, third.Status);
        Assert.AreEqual(-25m, third.Difference);
    }

    [TestMethod]
    public void Assess_DecimalBoundaryValues_DoesNotOverflow()
    {
        var budget = new ProductBudget();
        budget.SetInput("79228162514264337593543950335");
        var under = budget.Assess(0m);
        var equal = budget.Assess(decimal.MaxValue);
        budget.SetInput("0");
        var over = budget.Assess(decimal.MaxValue);

        Assert.IsNotNull(under);
        Assert.AreEqual(decimal.MaxValue, under.Difference);
        Assert.AreEqual(BudgetStatus.WithinBudget, under.Status);
        Assert.IsNotNull(equal);
        Assert.AreEqual(0m, equal.Difference);
        Assert.IsNotNull(over);
        Assert.AreEqual(decimal.MinValue, over.Difference);
        Assert.AreEqual(BudgetStatus.OverBudget, over.Status);
        Assert.EndsWith(" over budget", over.DifferenceDisplay);
    }
}
