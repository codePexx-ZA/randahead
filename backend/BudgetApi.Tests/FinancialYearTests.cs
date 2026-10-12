// Financial year tests: labels and start dates for a December and a February year end
using BudgetApi.Services;

namespace BudgetApi.Tests;

public class FinancialYearTests
{
    [Theory]
    [InlineData("FY2025", 12, 2025, 1)]
    [InlineData("FY2025", 2, 2024, 3)]
    public void StartAndLabel_RoundTrip(string label, int yearEndMonth, int startYear, int startMonth)
    {
        var start = FinancialYear.Start(label, yearEndMonth);

        Assert.Equal(new DateOnly(startYear, startMonth, 1), start);
        Assert.Equal(label, FinancialYear.Label(start, yearEndMonth));
    }
}
