// Forecast runs: reads saved runs in the Python service's shape and finds a unit's latest pair of runs
using System.Text.Json;
using System.Text.Json.Serialization;
using BudgetApi.Data;
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

public class ForecastRuns(BudgetDbContext db)
{
    public const string MovingAverage = "moving_average";
    public const string ExponentialSmoothing = "exponential_smoothing";

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
}
