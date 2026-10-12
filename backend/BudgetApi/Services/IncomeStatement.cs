// Income statement: the one definition of the 13 categories, the subtotals and their formulas, the display layout, the sign rule and the KPIs
namespace BudgetApi.Services;

public record LayoutItem(string? Category = null, string? Subtotal = null, string? Heading = null);

public record SubtotalFormula(string Code, string Name, (int Sign, string[] Codes)[] Terms);

public record KpiDefinition(string Code, string Name, string Group, string Unit, bool HigherIsBetter);

public record SubtotalCheck(bool Matches, decimal[] Diffs);

public record StatementTable(
    string[] Years,
    Dictionary<string, decimal[]> Totals,
    Dictionary<string, decimal[]> Subtotals,
    decimal[] TotalExpenses,
    Dictionary<string, SubtotalCheck>? Checks = null
);

public static class IncomeStatement
{
    public const string Revenue = "revenue";
    public const string GrossProfit = "gross_profit";
    public const string OperatingProfit = "operating_profit";
    public const string ProfitBeforeTax = "profit_before_tax";
    public const string NetProfit = "net_profit";

    public static readonly string[] IncomeCategories = [Revenue, "other_income"];

    public static readonly string[] OperatingExpenses =
    [
        "employee_costs",
        "occupancy",
        "electricity",
        "other_utilities",
        "transport_fuel",
        "marketing",
        "other_operating",
        "depreciation",
    ];

    // Same codes and order as line_category in database/init/02_reference_data.sql
    public static readonly string[] Categories = [Revenue, "other_income", "cost_of_sales", .. OperatingExpenses, "finance_costs", "income_tax"];

    // Each subtotal may use the ones before it
    public static readonly SubtotalFormula[] Subtotals =
    [
        new(GrossProfit, "Gross profit", [(1, [Revenue]), (-1, ["cost_of_sales"])]),
        new(OperatingProfit, "Operating profit", [(1, [GrossProfit]), (1, ["other_income"]), (-1, OperatingExpenses)]),
        new(ProfitBeforeTax, "Profit before tax", [(1, [OperatingProfit]), (-1, ["finance_costs"])]),
        new(NetProfit, "Net profit", [(1, [ProfitBeforeTax]), (-1, ["income_tax"])]),
    ];

    public static readonly LayoutItem[] Layout =
    [
        new(Category: Revenue),
        new(Category: "cost_of_sales"),
        new(Subtotal: GrossProfit),
        new(Category: "other_income"),
        new(Heading: "Operating expenses"),
        .. OperatingExpenses.Select(code => new LayoutItem(Category: code)),
        new(Subtotal: OperatingProfit),
        new(Category: "finance_costs"),
        new(Subtotal: ProfitBeforeTax),
        new(Category: "income_tax"),
        new(Subtotal: NetProfit),
    ];

    public static readonly Dictionary<string, (string Name, string Subtotal)> Margins = new()
    {
        ["net_margin"] = ("Net profit margin", NetProfit),
        ["operating_margin"] = ("Operating profit margin", OperatingProfit),
        ["gross_margin"] = ("Gross profit margin", GrossProfit),
    };

    private static readonly string[] ProfitKpis = [NetProfit, OperatingProfit, GrossProfit, ProfitBeforeTax];

    public static bool IsIncome(string category) => IncomeCategories.Contains(category);

    public static bool IsSubtotal(string code) => Subtotals.Any(s => s.Code == code);

    public static string SubtotalName(string code) => Subtotals.First(s => s.Code == code).Name;

    // In the order targets are listed: profits, margins, then the categories
    public static List<KpiDefinition> Kpis(IReadOnlyDictionary<string, string> categoryNames) =>
    [
        .. ProfitKpis.Select(code => new KpiDefinition(code, SubtotalName(code), "Profit and margins", "rand", true)),
        .. Margins.Select(m => new KpiDefinition(m.Key, m.Value.Name, "Profit and margins", "percent", true)),
        .. Categories.Select(code =>
            new KpiDefinition(code, categoryNames.GetValueOrDefault(code, code), "Income and expenses", "rand", IsIncome(code))
        ),
    ];

    public static Dictionary<string, decimal> WithSubtotals(IReadOnlyDictionary<string, decimal> categories)
    {
        var figures = new Dictionary<string, decimal>(categories);
        foreach (var subtotal in Subtotals)
        {
            figures[subtotal.Code] = subtotal.Terms.Sum(term => term.Sign * term.Codes.Sum(code => figures.GetValueOrDefault(code)));
        }
        return figures;
    }

    public static decimal? Kpi(string metric, IReadOnlyDictionary<string, decimal> figures)
    {
        if (Margins.TryGetValue(metric, out var margin))
        {
            var revenue = figures.GetValueOrDefault(Revenue);
            return revenue == 0 ? null : figures.GetValueOrDefault(margin.Subtotal) / revenue * 100;
        }
        return figures.TryGetValue(metric, out var value) ? value : null;
    }

    // Rands per category and year from the file's lines (amounts in file units, one per year, blank = 0)
    public static Dictionary<string, decimal[]> CategoryTotals(
        IEnumerable<(decimal?[] Values, IEnumerable<MappingPart> Mapping)> lines,
        int years,
        int scale
    )
    {
        var totals = Categories.ToDictionary(c => c, _ => new decimal[years]);
        foreach (var (values, mapping) in lines)
        {
            foreach (var part in mapping)
            {
                for (var k = 0; k < years; k++)
                {
                    totals[part.Category][k] += (values[k] ?? 0) * part.Percent / 100m * scale;
                }
            }
        }
        // Statements may show expenses in brackets or income as negative, so each side's overall sign sets its convention;
        // a category signed against its side (a tax credit, an other-income loss) stays negative
        static int SideSign(IEnumerable<decimal[]> side) => side.Sum(a => a.Sum()) < 0 ? -1 : 1;
        var incomeSign = SideSign(totals.Where(t => IsIncome(t.Key)).Select(t => t.Value));
        var expenseSign = SideSign(totals.Where(t => !IsIncome(t.Key)).Select(t => t.Value));
        foreach (var (category, amounts) in totals)
        {
            var sign = IsIncome(category) ? incomeSign : expenseSign;
            for (var k = 0; k < amounts.Length; k++)
            {
                amounts[k] *= sign;
            }
        }
        return totals;
    }

    public static StatementTable Table(string[] years, IReadOnlyDictionary<string, decimal[]> totals)
    {
        var perYear = years
            .Select((_, k) => WithSubtotals(Categories.ToDictionary(c => c, c => totals.TryGetValue(c, out var amounts) ? amounts[k] : 0)))
            .ToArray();
        return new StatementTable(
            years,
            Categories.ToDictionary(c => c, c => perYear.Select(f => f[c]).ToArray()),
            Subtotals.ToDictionary(s => s.Code, s => perYear.Select(f => f[s.Code]).ToArray()),
            perYear.Select(f => Categories.Where(c => !IsIncome(c)).Sum(c => f[c])).ToArray()
        );
    }

    // The statement behind a file's rows, with each of the file's own subtotal rows checked against the recalculation:
    // within one unit of the file's scale matches, and a later row for the same subtotal replaces an earlier one
    public static StatementTable FromRows(
        string[] years,
        IReadOnlyList<(string Kind, string? Subtotal, decimal?[] Values, IEnumerable<MappingPart>? Mapping)> rows,
        int scale
    )
    {
        var lines = rows.Where(r => r.Kind == "line").Select(r => (r.Values, r.Mapping ?? []));
        var table = Table(years, CategoryTotals(lines, years.Length, scale));
        var checks = new Dictionary<string, SubtotalCheck>();
        foreach (var row in rows.Where(r => r.Kind == "subtotal" && r.Subtotal is not null && IsSubtotal(r.Subtotal)))
        {
            var diffs = row.Values.Select((v, k) => table.Subtotals[row.Subtotal!][k] - (v ?? 0) * scale).ToArray();
            checks[row.Subtotal!] = new SubtotalCheck(diffs.All(d => Math.Abs(d) <= scale), diffs);
        }
        return table with { Checks = checks };
    }
}
