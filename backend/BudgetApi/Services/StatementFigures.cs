// Statement figures: a unit's approved history and budget figures per financial year, with subtotals, KPI values and target progress
using BudgetApi.Data;
using BudgetApi.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetApi.Services;

public record StatementLine(string Code, string Name, string Kind, bool TeamManaged, bool Subtotal);

public record TargetProgress(
    string Id,
    string MetricName,
    string Name,
    string Unit,
    bool HigherIsBetter,
    string Year,
    DateOnly TargetPeriod,
    decimal TargetValue,
    string? Source,
    decimal? Value,
    decimal? Gap,
    bool? Reached,
    string Status
);

public class StatementFigures(BudgetDbContext db)
{
    public const int HistoryYears = 5;

    public static readonly HashSet<string> IncomeCategories = ["revenue", "other_income"];

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

    public static readonly Dictionary<string, string> SubtotalNames = new()
    {
        ["net_profit"] = "Net profit",
        ["operating_profit"] = "Operating profit",
        ["gross_profit"] = "Gross profit",
        ["profit_before_tax"] = "Profit before tax",
    };

    public static readonly Dictionary<string, (string Name, string Subtotal)> Margins = new()
    {
        ["net_margin"] = ("Net margin", "net_profit"),
        ["operating_margin"] = ("Operating margin", "operating_profit"),
        ["gross_margin"] = ("Gross margin", "gross_profit"),
    };

    private static readonly string[] Layout =
    [
        "revenue",
        "cost_of_sales",
        "gross_profit",
        "other_income",
        .. OperatingExpenses,
        "operating_profit",
        "finance_costs",
        "profit_before_tax",
        "income_tax",
        "net_profit",
    ];

    public async Task<List<StatementLine>> LayoutAsync(CancellationToken cancellationToken)
    {
        var categories = await db.LineCategories.AsNoTracking().ToDictionaryAsync(c => c.CategoryCode, cancellationToken);
        return Layout
            .Select(code =>
                SubtotalNames.TryGetValue(code, out var name)
                    ? new StatementLine(code, name, "total", false, true)
                    : new StatementLine(
                        code,
                        categories[code].DisplayName,
                        IncomeCategories.Contains(code) ? "income" : "expense",
                        categories[code].TeamManaged,
                        false
                    )
            )
            .ToList();
    }

    public async Task<SortedDictionary<DateOnly, Dictionary<string, decimal>>> ApprovedHistoryAsync(
        string unitId,
        CancellationToken cancellationToken
    )
    {
        var items = await db
            .FinancialLineItems.AsNoTracking()
            .Where(i => i.Frequency == "annual")
            .Join(
                db.BudgetSubmissions.Where(s => s.UnitId == unitId && s.Status == "approved"),
                i => i.SubmissionId,
                s => s.SubmissionId,
                (i, s) => new { i.Category, i.PeriodStart, i.Amount, s.SubmittedAt }
            )
            .ToListAsync(cancellationToken);

        var periods = items.Select(i => i.PeriodStart).Distinct().OrderDescending().Take(HistoryYears).ToHashSet();
        var history = new SortedDictionary<DateOnly, Dictionary<string, decimal>>();
        foreach (var period in items.Where(i => periods.Contains(i.PeriodStart)).GroupBy(i => i.PeriodStart))
        {
            history[period.Key] = WithSubtotals(
                period.GroupBy(i => i.Category).ToDictionary(g => g.Key, g => g.MaxBy(i => i.SubmittedAt)!.Amount)
            );
        }
        return history;
    }

    public async Task<SortedDictionary<DateOnly, Dictionary<string, decimal>>> BudgetAsync(
        BudgetSelection budget,
        CancellationToken cancellationToken
    )
    {
        var values = await db
            .ForecastValues.AsNoTracking()
            .Where(v => v.RunId == budget.MaRunId || v.RunId == budget.EsRunId)
            .Select(v => new { v.RunId, v.Category, v.ForecastPeriod, v.BaselineAmount })
            .ToListAsync(cancellationToken);

        var figures = new SortedDictionary<DateOnly, Dictionary<string, decimal>>();
        foreach (var period in values.GroupBy(v => v.ForecastPeriod))
        {
            figures[period.Key] = WithSubtotals(
                period
                    .GroupBy(v => v.Category)
                    .ToDictionary(
                        g => g.Key,
                        g =>
                        {
                            var ma = g.FirstOrDefault(v => v.RunId == budget.MaRunId)?.BaselineAmount ?? 0;
                            var es = g.FirstOrDefault(v => v.RunId == budget.EsRunId)?.BaselineAmount ?? 0;
                            return budget.BudgetMethod switch
                            {
                                ForecastRuns.MovingAverage => ma,
                                ForecastRuns.ExponentialSmoothing => es,
                                _ => Math.Round((ma + es) / 2, MidpointRounding.AwayFromZero),
                            };
                        }
                    )
            );
        }
        return figures;
    }

    public async Task<SortedDictionary<DateOnly, Dictionary<string, decimal>>?> LatestBudgetAsync(
        string unitId,
        CancellationToken cancellationToken
    )
    {
        var latest = await db
            .BudgetSelections.AsNoTracking()
            .Where(b => b.UnitId == unitId && b.Status != "superseded")
            .OrderByDescending(b => b.ChosenAt)
            .FirstOrDefaultAsync(cancellationToken);
        return latest is null ? null : await BudgetAsync(latest, cancellationToken);
    }

    public async Task<List<TargetProgress>> TargetsAsync(
        string unitId,
        SortedDictionary<DateOnly, Dictionary<string, decimal>> history,
        SortedDictionary<DateOnly, Dictionary<string, decimal>>? budget,
        Func<DateOnly, string> label,
        CancellationToken cancellationToken
    )
    {
        var targets = await db.KpiTargets.AsNoTracking().Where(t => t.UnitId == unitId).ToListAsync(cancellationToken);
        var categoryNames = (await db.LineCategories.AsNoTracking().OrderBy(c => c.SortOrder).ToListAsync(cancellationToken))
            .ToDictionary(c => c.CategoryCode, c => c.DisplayName);
        var order = SubtotalNames.Keys.Concat(Margins.Keys).Concat(categoryNames.Keys).ToList();
        int KpiOrder(string metric) => order.IndexOf(metric) is var at and >= 0 ? at : order.Count;

        return targets
            .Select(t => Progress(t, history, budget, categoryNames, label))
            .OrderByDescending(t => t.TargetPeriod)
            .ThenBy(t => KpiOrder(t.MetricName))
            .ToList();
    }

    public static object ApprovedBudgetSummary(BudgetSelection b)
    {
        var approval = b.Approvals.Where(a => a.Decision == "approved").MaxBy(a => a.ActedAt)!;
        return new
        {
            id = b.BudgetId,
            method = b.BudgetMethod,
            b.Status,
            approvedBy = DisplayText.Person(approval.Actor),
            approvedOn = DisplayText.Day(approval.ActedAt),
        };
    }

    private static TargetProgress Progress(
        KpiTarget t,
        SortedDictionary<DateOnly, Dictionary<string, decimal>> history,
        SortedDictionary<DateOnly, Dictionary<string, decimal>>? budget,
        Dictionary<string, string> categoryNames,
        Func<DateOnly, string> label
    )
    {
        var name = SubtotalNames.TryGetValue(t.MetricName, out var subtotal) ? subtotal
            : Margins.TryGetValue(t.MetricName, out var margin) ? margin.Name
            : categoryNames.GetValueOrDefault(t.MetricName, t.MetricName);
        var higher = HigherIsBetter(t.MetricName);
        var (source, figures) = history.TryGetValue(t.TargetPeriod, out var actual) ? ("actual", actual)
            : budget is not null && budget.TryGetValue(t.TargetPeriod, out var planned) ? ("budget", planned)
            : ((string?)null, (Dictionary<string, decimal>?)null);
        var value = figures is null ? null : Kpi(t.MetricName, figures);
        if (value is null)
        {
            return new TargetProgress(t.TargetId, t.MetricName, name, t.MeasurementUnit, higher, label(t.TargetPeriod), t.TargetPeriod, t.TargetValue, null, null, null, null, "none");
        }

        var gap = value.Value - t.TargetValue;
        var reached = higher ? gap >= 0 : gap <= 0;
        var status = source == "actual" ? (reached ? "met" : "missed") : (reached ? "on_track" : "off_track");
        return new TargetProgress(
            t.TargetId,
            t.MetricName,
            name,
            t.MeasurementUnit,
            higher,
            label(t.TargetPeriod),
            t.TargetPeriod,
            t.TargetValue,
            source,
            Math.Round(value.Value, 2),
            Math.Round(gap, 2),
            reached,
            status
        );
    }

    public static Dictionary<string, decimal> WithSubtotals(Dictionary<string, decimal> categories)
    {
        decimal Get(string code) => categories.GetValueOrDefault(code);
        var figures = new Dictionary<string, decimal>(categories);
        figures["gross_profit"] = Get("revenue") - Get("cost_of_sales");
        figures["operating_profit"] = figures["gross_profit"] + Get("other_income") - OperatingExpenses.Sum(Get);
        figures["profit_before_tax"] = figures["operating_profit"] - Get("finance_costs");
        figures["net_profit"] = figures["profit_before_tax"] - Get("income_tax");
        return figures;
    }

    public static decimal? Kpi(string metric, IReadOnlyDictionary<string, decimal> figures)
    {
        if (Margins.TryGetValue(metric, out var margin))
        {
            var revenue = figures.GetValueOrDefault("revenue");
            return revenue == 0 ? null : figures.GetValueOrDefault(margin.Subtotal) / revenue * 100;
        }
        return figures.TryGetValue(metric, out var value) ? value : null;
    }

    public static bool HigherIsBetter(string metric) =>
        IncomeCategories.Contains(metric) || SubtotalNames.ContainsKey(metric) || Margins.ContainsKey(metric);

    public static decimal? PercentChange(decimal? value, decimal? from) =>
        value is null || from is null or 0 ? null : Math.Round((value.Value - from.Value) / Math.Abs(from.Value) * 100, 1);
}
