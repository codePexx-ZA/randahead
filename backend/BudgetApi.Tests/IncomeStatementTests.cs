// Income statement tests: category totals and their sign rule, subtotals, KPIs, target progress and the template's formulas
using BudgetApi.Models;
using BudgetApi.Services;
using ClosedXML.Excel;

namespace BudgetApi.Tests;

public class IncomeStatementTests
{
    private static readonly DateOnly Year = new(2025, 1, 1);

    [Theory]
    [InlineData(new[] { "revenue:1000", "revenue:-50" }, 1, new[] { "revenue:950" })]
    [InlineData(new[] { "cost_of_sales:-200", "cost_of_sales:-10" }, 1000, new[] { "cost_of_sales:210000" })]
    [InlineData(
        new[] { "revenue:1000", "other_income:50", "cost_of_sales:-400", "employee_costs:-100" },
        1,
        new[] { "revenue:1000", "other_income:50", "cost_of_sales:400", "employee_costs:100" }
    )]
    [InlineData(
        new[] { "revenue:-1000", "other_income:-20", "cost_of_sales:600", "income_tax:90" },
        1,
        new[] { "revenue:1000", "other_income:20", "cost_of_sales:600", "income_tax:90" }
    )]
    [InlineData(
        new[] { "revenue:1000", "other_income:-30", "cost_of_sales:600" },
        1,
        new[] { "revenue:1000", "other_income:-30", "cost_of_sales:600" }
    )]
    public void CategoryTotals_AppliesEachSidesSignConvention(string[] lines, int scale, string[] expected)
    {
        var totals = IncomeStatement.CategoryTotals(lines.Select(Line), 1, scale);

        Assert.All(expected.Select(Parse), e => Assert.Equal(e.Amount, totals[e.Category][0]));
        Assert.Equal(IncomeStatement.Categories, totals.Keys);
    }

    [Fact]
    public void CategoryTotals_TaxCreditInBracketedStatement_StaysNegative()
    {
        var totals = IncomeStatement.CategoryTotals(
            new[] { "revenue:1000", "cost_of_sales:-600", "income_tax:30" }.Select(Line),
            1,
            1
        );

        Assert.Equal(1000m, totals["revenue"][0]);
        Assert.Equal(600m, totals["cost_of_sales"][0]);
        Assert.Equal(-30m, totals["income_tax"][0]);
    }

    [Fact]
    public void CategoryTotals_SplitLineAndBlankYear_ShareByPercentAndCountAsZero()
    {
        (decimal?[], IEnumerable<MappingPart>)[] lines =
        [
            ([1000m, 2000m], [new MappingPart("revenue", 100)]),
            ([100m, null], [new MappingPart("occupancy", 60), new MappingPart("other_utilities", 40)]),
        ];

        var totals = IncomeStatement.CategoryTotals(lines, 2, 1000);

        Assert.Equal([60_000m, 0m], totals["occupancy"]);
        Assert.Equal([40_000m, 0m], totals["other_utilities"]);
    }

    [Theory]
    [InlineData(1000, 400, 50, 100, 200, 20, 90, 600, 350, 330, 240)]
    [InlineData(1000, 1200, 0, 0, 0, 0, -10, -200, -200, -200, -190)]
    [InlineData(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)]
    public void WithSubtotals_AddsUpLikeTheStatement(
        int revenue,
        int costOfSales,
        int otherIncome,
        int employeeCosts,
        int depreciation,
        int financeCosts,
        int incomeTax,
        int grossProfit,
        int operatingProfit,
        int profitBeforeTax,
        int netProfit
    )
    {
        var figures = IncomeStatement.WithSubtotals(
            new Dictionary<string, decimal>
            {
                ["revenue"] = revenue,
                ["cost_of_sales"] = costOfSales,
                ["other_income"] = otherIncome,
                ["employee_costs"] = employeeCosts,
                ["depreciation"] = depreciation,
                ["finance_costs"] = financeCosts,
                ["income_tax"] = incomeTax,
            }
        );

        Assert.Equal(grossProfit, figures["gross_profit"]);
        Assert.Equal(operatingProfit, figures["operating_profit"]);
        Assert.Equal(profitBeforeTax, figures["profit_before_tax"]);
        Assert.Equal(netProfit, figures["net_profit"]);
    }

    [Fact]
    public void FromRows_ChecksTheFilesSubtotalsWithinOneUnitOfItsScale()
    {
        (string, string?, decimal?[], IEnumerable<MappingPart>?)[] rows =
        [
            ("line", null, [1000m, 1100m], [new MappingPart("revenue", 100)]),
            ("line", null, [-400m, -450m], [new MappingPart("cost_of_sales", 100)]),
            ("subtotal", "gross_profit", [600.4m, 640m], null),
            ("subtotal", "net_profit", [600m, null], null),
        ];

        var statement = IncomeStatement.FromRows(["FY2024", "FY2025"], rows, 1000);

        Assert.Equal([600_000m, 650_000m], statement.Subtotals["gross_profit"]);
        Assert.Equal([400_000m, 450_000m], statement.TotalExpenses);
        Assert.False(statement.Checks!["gross_profit"].Matches);
        Assert.Equal([-400m, 10_000m], statement.Checks["gross_profit"].Diffs);
        Assert.False(statement.Checks["net_profit"].Matches);
        Assert.Equal([0m, 650_000m], statement.Checks["net_profit"].Diffs);
    }

    [Theory]
    [InlineData("net_margin", 1000, 100, 10.0)]
    [InlineData("gross_margin", 0, 100, null)]
    [InlineData("revenue", 1000, 100, 1000.0)]
    [InlineData("not_a_kpi", 1000, 100, null)]
    public void Kpi_MarginsArePercentOfRevenue(string metric, int revenue, int profit, double? expected)
    {
        var figures = new Dictionary<string, decimal> { ["revenue"] = revenue, ["net_profit"] = profit, ["gross_profit"] = profit };

        Assert.Equal(expected, (double?)IncomeStatement.Kpi(metric, figures));
    }

    [Fact]
    public void Kpis_ListProfitsThenMarginsThenEveryCategory()
    {
        var kpis = IncomeStatement.Kpis(new Dictionary<string, string> { ["revenue"] = "Revenue" });

        Assert.Equal(20, kpis.Count);
        Assert.Equal(["net_profit", "operating_profit", "gross_profit", "profit_before_tax", "net_margin"], kpis.Take(5).Select(k => k.Code));
        Assert.Equal(new KpiDefinition("net_margin", "Net profit margin", "Profit and margins", "percent", true), kpis[4]);
        Assert.Equal("Revenue", kpis.Single(k => k.Code == "revenue").Name);
        Assert.False(kpis.Single(k => k.Code == "cost_of_sales").HigherIsBetter);
    }

    [Theory]
    [InlineData("revenue", 900, 1000, null, "met", true)]
    [InlineData("revenue", 1100, 1000, null, "missed", false)]
    [InlineData("cost_of_sales", 500, 600, null, "missed", false)]
    [InlineData("cost_of_sales", 700, 600, null, "met", true)]
    [InlineData("revenue", 900, null, 1000, "on_track", true)]
    [InlineData("revenue", 1100, null, 1000, "off_track", false)]
    [InlineData("revenue", 900, null, null, "none", null)]
    public void Progress_ChecksActualsThenBudget(string metric, int target, int? actual, int? budget, string status, bool? reached)
    {
        SortedDictionary<DateOnly, Dictionary<string, decimal>> Figures(int? value) =>
            value is null ? new() : new() { [Year] = new() { [metric] = value.Value } };
        var kpi = IncomeStatement.Kpis(new Dictionary<string, string>()).Single(k => k.Code == metric);
        var t = new KpiTarget { MetricName = metric, TargetValue = target, MeasurementUnit = "rand", TargetPeriod = Year };

        var progress = StatementFigures.Progress(t, Figures(actual), Figures(budget), kpi, _ => "FY2025");

        Assert.Equal(status, progress.Status);
        Assert.Equal(reached, progress.Reached);
    }

    [Theory]
    [InlineData("Gross profit", "B5-B6")]
    [InlineData("Operating profit", "B7+B8-SUM(B10:B17)")]
    [InlineData("Profit before tax", "B18-B19")]
    [InlineData("Net profit", "B20-B21")]
    public void Template_SubtotalRowsAddUpTheLinesAboveThem(string subtotal, string formula)
    {
        var names = IncomeStatement.Categories.ToDictionary(c => c, c => c);
        using var workbook = new XLWorkbook(new MemoryStream(StatementTemplate.Build("Test", 12, names, new DateOnly(2026, 6, 1))));

        var row = workbook.Worksheet(1).Column(1).CellsUsed().Single(c => c.GetString() == subtotal).Address.RowNumber;

        Assert.Equal(formula, workbook.Worksheet(1).Cell(row, 2).FormulaA1);
    }

    private static (string Category, decimal Amount) Parse(string line) =>
        (line.Split(':')[0], decimal.Parse(line.Split(':')[1], System.Globalization.CultureInfo.InvariantCulture));

    private static (decimal?[], IEnumerable<MappingPart>) Line(string line)
    {
        var (category, amount) = Parse(line);
        return ([amount], [new MappingPart(category, 100)]);
    }
}
