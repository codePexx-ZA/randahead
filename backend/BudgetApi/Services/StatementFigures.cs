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

    public async Task<List<LineCategory>> CategoriesAsync(CancellationToken cancellationToken) =>
        await db.LineCategories.AsNoTracking().OrderBy(c => c.SortOrder).ToListAsync(cancellationToken);

    public async Task<List<KpiDefinition>> KpisAsync(CancellationToken cancellationToken) =>
        IncomeStatement.Kpis((await CategoriesAsync(cancellationToken)).ToDictionary(c => c.CategoryCode, c => c.DisplayName));

    public async Task<List<StatementLine>> LayoutAsync(CancellationToken cancellationToken)
    {
        var categories = (await CategoriesAsync(cancellationToken)).ToDictionary(c => c.CategoryCode);
        return IncomeStatement
            .Layout.Where(item => item.Heading is null)
            .Select(item =>
                item.Subtotal is { } subtotal
                    ? new StatementLine(subtotal, IncomeStatement.SubtotalName(subtotal), "total", false, true)
                    : new StatementLine(
                        item.Category!,
                        categories[item.Category!].DisplayName,
                        IncomeStatement.IsIncome(item.Category!) ? "income" : "expense",
                        categories[item.Category!].TeamManaged,
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
        var amounts = await ApprovedAmountsAsync(unitId, cancellationToken);
        return new SortedDictionary<DateOnly, Dictionary<string, decimal>>(
            amounts.TakeLast(HistoryYears).ToDictionary(year => year.Key, year => IncomeStatement.WithSubtotals(year.Value))
        );
    }

    // Every approved year's category amounts; where approved uploads overlap, the latest upload wins
    public async Task<SortedDictionary<DateOnly, Dictionary<string, decimal>>> ApprovedAmountsAsync(
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

        var amounts = new SortedDictionary<DateOnly, Dictionary<string, decimal>>();
        foreach (var period in items.GroupBy(i => i.PeriodStart))
        {
            amounts[period.Key] = period.GroupBy(i => i.Category).ToDictionary(g => g.Key, g => g.MaxBy(i => i.SubmittedAt)!.Amount);
        }
        return amounts;
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
            figures[period.Key] = IncomeStatement.WithSubtotals(
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
                                _ => ForecastRuns.Average(ma, es),
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
        var kpis = await KpisAsync(cancellationToken);
        int KpiOrder(string metric) => kpis.FindIndex(k => k.Code == metric) is var at and >= 0 ? at : kpis.Count;

        return targets
            .Select(t => Progress(t, history, budget, kpis.FirstOrDefault(k => k.Code == t.MetricName), label))
            .OrderByDescending(t => t.TargetPeriod)
            .ThenBy(t => KpiOrder(t.MetricName))
            .ToList();
    }

    // Each KPI's value in every actual year, then every budget year after them, for the target form's hint
    public static IEnumerable<object> KpiFigures(
        SortedDictionary<DateOnly, Dictionary<string, decimal>> history,
        SortedDictionary<DateOnly, Dictionary<string, decimal>>? budget,
        IEnumerable<KpiDefinition> kpis,
        Func<DateOnly, string> label
    ) =>
        history
            .Select(h => (h.Key, Source: "actual", Figures: h.Value))
            .Concat(budget?.Where(b => !history.ContainsKey(b.Key)).Select(b => (b.Key, Source: "budget", Figures: b.Value)) ?? [])
            .Select(y => new
            {
                year = label(y.Key),
                source = y.Source,
                values = kpis.ToDictionary(k => k.Code, k => IncomeStatement.Kpi(k.Code, y.Figures)),
            });

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

    internal static TargetProgress Progress(
        KpiTarget t,
        SortedDictionary<DateOnly, Dictionary<string, decimal>> history,
        SortedDictionary<DateOnly, Dictionary<string, decimal>>? budget,
        KpiDefinition? kpi,
        Func<DateOnly, string> label
    )
    {
        var name = kpi?.Name ?? t.MetricName;
        var higher = kpi?.HigherIsBetter ?? false;
        var (source, figures) = history.TryGetValue(t.TargetPeriod, out var actual) ? ("actual", actual)
            : budget is not null && budget.TryGetValue(t.TargetPeriod, out var planned) ? ("budget", planned)
            : ((string?)null, (Dictionary<string, decimal>?)null);
        var value = figures is null || kpi is null ? null : IncomeStatement.Kpi(t.MetricName, figures);
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

    public static decimal? PercentChange(decimal? value, decimal? from) =>
        value is null || from is null or 0 ? null : Math.Round((value.Value - from.Value) / Math.Abs(from.Value) * 100, 1);
}
