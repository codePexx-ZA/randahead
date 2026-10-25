// Statement figure tests: KPI targets follow the approved budget, not a newer waiting or rejected one
using BudgetApi.Data;
using BudgetApi.Models;
using BudgetApi.Services;
using Microsoft.EntityFrameworkCore;

namespace BudgetApi.Tests;

public class StatementFiguresTests
{
    private static readonly DateOnly Period = new(2025, 3, 1);

    [Fact]
    public async Task Targets_KeepTheApprovedBudget_WhenANewerOneIsRejected()
    {
        await using var db = NewDb();
        var approved = AddBudget(db, "approved", 10_409_375m, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        AddBudget(db, "rejected", 10_725_000m, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        AddBudget(db, "submitted", 9_000_000m, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        AddBudget(db, "superseded", 8_000_000m, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));
        db.KpiTargets.Add(new KpiTarget
        {
            UnitId = "unit",
            MetricName = IncomeStatement.Revenue,
            TargetValue = 10_000_000m,
            MeasurementUnit = "rand",
            TargetPeriod = Period,
        });
        await db.SaveChangesAsync();

        var figures = new StatementFigures(db);
        var budget = await figures.ApprovedBudgetAsync("unit", CancellationToken.None);
        Assert.NotNull(budget);
        var amounts = await figures.BudgetAsync(budget, CancellationToken.None);
        var targets = await figures.TargetsAsync(
            "unit",
            new SortedDictionary<DateOnly, Dictionary<string, decimal>>(),
            amounts,
            _ => "FY2026",
            CancellationToken.None
        );

        Assert.Equal(approved.BudgetId, budget.BudgetId);
        var target = Assert.Single(targets);
        Assert.Equal(10_409_375m, target.Value);
        Assert.Equal("budget", target.Source);
        Assert.Equal("on_track", target.Status);
    }

    [Fact]
    public async Task ApprovedBudget_IsMissing_WhenNothingIsApproved()
    {
        await using var db = NewDb();
        AddBudget(db, "rejected", 10_725_000m, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        await db.SaveChangesAsync();

        var budget = await new StatementFigures(db).ApprovedBudgetAsync("unit", CancellationToken.None);

        Assert.Null(budget);
    }

    private static BudgetDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<BudgetDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new BudgetDbContext(options);
    }

    private static BudgetSelection AddBudget(BudgetDbContext db, string status, decimal revenue, DateTime chosenAt)
    {
        var ma = Guid.NewGuid().ToString("N");
        var es = Guid.NewGuid().ToString("N");
        db.ForecastValues.Add(new ForecastValue
        {
            RunId = ma,
            Category = IncomeStatement.Revenue,
            ForecastPeriod = Period,
            BaselineAmount = revenue,
        });
        db.ForecastValues.Add(new ForecastValue
        {
            RunId = es,
            Category = IncomeStatement.Revenue,
            ForecastPeriod = Period,
            BaselineAmount = revenue + 1,
        });
        var budget = new BudgetSelection
        {
            UnitId = "unit",
            Status = status,
            BudgetMethod = ForecastRuns.MovingAverage,
            MaRunId = ma,
            EsRunId = es,
            ChosenBy = "chooser",
            ChosenAt = chosenAt,
        };
        db.BudgetSelections.Add(budget);
        return budget;
    }
}
