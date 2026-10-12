// Statement tests: reading the sample income statement and adding up category totals
using BudgetApi.Controllers;
using BudgetApi.Services;

namespace BudgetApi.Tests;

public class StatementTests
{
    private static readonly string[] Categories = ["revenue", "cost_of_sales"];

    [Fact]
    public void Read_SampleStatement_FindsEveryYearAndMatchesEveryLine()
    {
        using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "samples", "Karoo_Outdoor_IS_FY2021-FY2025.xlsx"));

        var preview = StatementImporter.Read(file, "Karoo_Outdoor_IS_FY2021-FY2025.xlsx", new Dictionary<string, MappingPart[]>());

        Assert.Equal(["FY2021", "FY2022", "FY2023", "FY2024", "FY2025"], preview.Years);
        var lines = preview.Rows.Where(r => r.Kind == "line").ToList();
        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.NotNull(line.Mapping));
    }

    [Fact]
    public void CategoryTotals_ContraLine_IsSubtracted()
    {
        SubmissionRowRequest[] rows =
        [
            Line("Sales", "revenue", 1000m),
            Line("Less: returns", "revenue", -50m),
        ];

        var totals = SubmissionsController.CategoryTotals(rows, [0], 1, Categories);

        Assert.Equal(950m, totals["revenue"][0]);
    }

    [Fact]
    public void CategoryTotals_NegativeExpenses_BecomePositiveTotal()
    {
        SubmissionRowRequest[] rows =
        [
            Line("Purchases", "cost_of_sales", -200m),
            Line("Freight in", "cost_of_sales", -10m),
        ];

        var totals = SubmissionsController.CategoryTotals(rows, [0], 1000, Categories);

        Assert.Equal(210_000m, totals["cost_of_sales"][0]);
    }

    private static SubmissionRowRequest Line(string label, string category, decimal amount) =>
        new(label, "line", null, [amount], [new MappingPart(category, 100m)], false);
}
