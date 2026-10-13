using System.Globalization;

namespace eShop.WebAppComponents.Services;

public enum BudgetStatus
{
    WithinBudget,
    OverBudget,
    PriceUnavailable
}

public sealed record BudgetAssessment(BudgetStatus Status, decimal? Difference)
{
    public string StatusDisplay => Status switch
    {
        BudgetStatus.WithinBudget => "Within budget",
        BudgetStatus.OverBudget => "Over budget",
        BudgetStatus.PriceUnavailable => "Price unavailable",
        _ => throw new InvalidOperationException("Unknown budget status.")
    };

    public string DifferenceDisplay => Difference switch
    {
        null => "A valid product price is needed to check its budget.",
        0m => "$0.00 (exactly at budget)",
        > 0m => $"${Difference.Value.ToPriceDisplay()} under budget",
        _ => $"${decimal.Negate(Difference.Value).ToPriceDisplay()} over budget"
    };
}

public sealed class ProductBudget
{
    public decimal? Amount { get; private set; }
    public string? ValidationMessage { get; private set; }

    public void SetInput(string? input)
    {
        Amount = null;
        ValidationMessage = null;
        var text = input?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var separator = text.IndexOf('.');
        if (separator >= 0 && text.Length - separator - 1 > 2)
        {
            ValidationMessage = "Use no more than two decimal places for your budget.";
            return;
        }

        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) || amount < 0)
        {
            ValidationMessage = "Enter a non-negative dollar amount within the supported range, for example 100 or 100.50. Do not include commas or a currency symbol.";
            return;
        }

        // Decimal parsing can round high-magnitude fractional amounts; do not silently change the budget.
        var parts = text.Split('.');
        var whole = parts[0].TrimStart('0');
        var cents = parts.Length == 2 ? parts[1].PadRight(2, '0') : "00";
        var normalized = $"{(whole.Length == 0 ? "0" : whole)}.{cents}";
        if (amount.ToString("F2", CultureInfo.InvariantCulture) != normalized)
        {
            ValidationMessage = "This budget cannot be represented exactly. Please enter a smaller amount.";
            return;
        }

        Amount = amount;
    }

    public BudgetAssessment? Assess(decimal? price)
    {
        if (Amount is not decimal budget)
        {
            return null;
        }

        if (price is not decimal productPrice || productPrice < 0)
        {
            return new BudgetAssessment(BudgetStatus.PriceUnavailable, null);
        }

        var difference = budget - productPrice;
        return new BudgetAssessment(difference >= 0 ? BudgetStatus.WithinBudget : BudgetStatus.OverBudget, difference);
    }
}
