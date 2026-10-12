// Forecast runs: forecasts a unit's approved history through the Python service, saves the pair of runs and reads them back
using System.Text.Json;
using System.Text.Json.Serialization;
using BudgetApi.Data;
using BudgetApi.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetApi.Services;

public record ForecastValueView(
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("forecast_period")] DateOnly ForecastPeriod,
    [property: JsonPropertyName("baseline_amount")] decimal BaselineAmount,
    [property: JsonPropertyName("adjusted_amount")] decimal? AdjustedAmount
);

public record ForecastRunView(
    [property: JsonPropertyName("run_id")] string RunId,
    [property: JsonPropertyName("unit_id")] string UnitId,
    [property: JsonPropertyName("forecasting_method")] string ForecastingMethod,
    [property: JsonPropertyName("history_start")] DateOnly HistoryStart,
    [property: JsonPropertyName("history_end")] DateOnly HistoryEnd,
    [property: JsonPropertyName("parameters")] JsonElement Parameters,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt,
    [property: JsonPropertyName("values")] ForecastValueView[] Values
);

public record ForecastComparison(string[] Years, Dictionary<string, StatementTable> Methods);

public record ForecastPair(IReadOnlyList<ForecastRunView> Runs, ForecastComparison Comparison);

public class ForecastRuns(BudgetDbContext db, StatementFigures figures, ForecastClient client)
{
    public const string MovingAverage = "moving_average";
    public const string ExponentialSmoothing = "exponential_smoothing";

    // Adds both runs to the context unsaved, so the caller saves them in one go with its audit entry
    public async Task<List<ForecastRun>> CreateAsync(
        string unitId,
        int horizon,
        int windowSize,
        double alpha,
        CancellationToken cancellationToken
    )
    {
        var amounts = await figures.ApprovedAmountsAsync(unitId, cancellationToken);
        var history = amounts
            .SelectMany(year => year.Value.Select(category => (category.Key, Point: new ForecastPoint(year.Key, category.Value))))
            .GroupBy(entry => entry.Key)
            .ToDictionary(group => group.Key, group => group.Select(entry => entry.Point).ToArray());
        var output = await client.ForecastAsync(new ForecastInput(history, horizon, windowSize, alpha), cancellationToken);

        var createdAt = DateTime.UtcNow;
        var runs = output
            .Forecasts.Select(method => new ForecastRun
            {
                UnitId = unitId,
                ForecastingMethod = method.Key,
                HistoryStart = output.HistoryStart,
                HistoryEnd = output.HistoryEnd,
                Parameters = JsonSerializer.Serialize(
                    new Dictionary<string, object>
                    {
                        [method.Key == MovingAverage ? "window_size" : "alpha"] = method.Key == MovingAverage ? (object)windowSize : alpha,
                        ["frequency"] = "annual",
                        ["horizon"] = horizon,
                        ["warnings"] = output.Warnings,
                    }
                ),
                CreatedAt = createdAt,
                Values = method
                    .Value.SelectMany(category =>
                        category.Value.Select((amount, k) => new ForecastValue
                        {
                            Category = category.Key,
                            ForecastPeriod = output.Periods[k],
                            BaselineAmount = amount,
                        })
                    )
                    .ToList(),
            })
            .ToList();
        db.ForecastRuns.AddRange(runs);
        return runs;
    }

    public async Task<List<ForecastRunView>> LoadAsync(string unitId, IReadOnlyCollection<string> runIds, CancellationToken cancellationToken)
    {
        var categories = await db.LineCategories.AsNoTracking().ToDictionaryAsync(c => c.CategoryCode, cancellationToken);
        var runs = await db
            .ForecastRuns.AsNoTracking()
            .Include(r => r.Values)
            .Where(r => r.UnitId == unitId && runIds.Contains(r.RunId))
            .ToListAsync(cancellationToken);

        return runs
            .OrderBy(r => r.ForecastingMethod == MovingAverage ? 0 : 1)
            .Select(r => new ForecastRunView(
                r.RunId,
                r.UnitId,
                r.ForecastingMethod,
                r.HistoryStart,
                r.HistoryEnd,
                JsonDocument.Parse(r.Parameters).RootElement.Clone(),
                r.CreatedAt,
                r.Values
                    .OrderBy(v => categories.TryGetValue(v.Category, out var c) ? c.SortOrder : short.MaxValue)
                    .ThenBy(v => v.ForecastPeriod)
                    .Select(v => new ForecastValueView(
                        v.Category,
                        categories.TryGetValue(v.Category, out var c) ? c.DisplayName : v.Category,
                        v.ForecastPeriod,
                        v.BaselineAmount,
                        v.AdjustedAmount
                    ))
                    .ToArray()
            ))
            .ToList();
    }

    public async Task<ForecastPair> LoadPairAsync(
        string unitId,
        IReadOnlyCollection<string> runIds,
        int yearEndMonth,
        CancellationToken cancellationToken
    )
    {
        var pair = await LoadAsync(unitId, runIds, cancellationToken);
        return new ForecastPair(pair, Compare(pair, yearEndMonth));
    }

    public async Task<List<string>> LatestPairAsync(string unitId, CancellationToken cancellationToken)
    {
        var newest = await db
            .ForecastRuns.Where(r => r.UnitId == unitId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => (DateTime?)r.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (newest is null)
        {
            return [];
        }
        var pair = await db
            .ForecastRuns.Where(r => r.UnitId == unitId && r.CreatedAt == newest)
            .Select(r => new { r.RunId, r.ForecastingMethod })
            .ToListAsync(cancellationToken);
        return IsPair(pair.Select(r => r.ForecastingMethod)) ? pair.Select(r => r.RunId).ToList() : [];
    }

    public Task<DateOnly?> LatestApprovedPeriodAsync(string unitId, CancellationToken cancellationToken) =>
        db
            .FinancialLineItems.Where(i =>
                db.BudgetSubmissions.Any(s => s.SubmissionId == i.SubmissionId && s.UnitId == unitId && s.Status == "approved")
            )
            .MaxAsync(i => (DateOnly?)i.PeriodStart, cancellationToken);

    public static bool IsPair(IEnumerable<string> methods) =>
        methods.Order().SequenceEqual([ExponentialSmoothing, MovingAverage]);

    public static decimal Average(decimal ma, decimal es) => Math.Round((ma + es) / 2, MidpointRounding.AwayFromZero);

    // Both runs and their average as statements over the forecast years
    public static ForecastComparison Compare(IReadOnlyList<ForecastRunView> pair, int yearEndMonth)
    {
        var horizon = pair[0].Parameters.GetProperty("horizon").GetInt32();
        var periods = pair[0].Values.Select(v => v.ForecastPeriod).Distinct().Order().Take(horizon).ToArray();
        var years = periods.Select(p => FinancialYear.Label(p, yearEndMonth)).ToArray();
        Dictionary<string, decimal[]> Totals(string method)
        {
            var values = pair.Single(r => r.ForecastingMethod == method).Values;
            return IncomeStatement.Categories.ToDictionary(
                c => c,
                c => periods.Select(p => values.FirstOrDefault(v => v.Category == c && v.ForecastPeriod == p)?.BaselineAmount ?? 0).ToArray()
            );
        }
        var ma = Totals(MovingAverage);
        var es = Totals(ExponentialSmoothing);
        var combined = ma.ToDictionary(t => t.Key, t => t.Value.Select((v, k) => Average(v, es[t.Key][k])).ToArray());
        return new ForecastComparison(
            years,
            new()
            {
                [MovingAverage] = IncomeStatement.Table(years, ma),
                [ExponentialSmoothing] = IncomeStatement.Table(years, es),
                ["combined"] = IncomeStatement.Table(years, combined),
            }
        );
    }
}
