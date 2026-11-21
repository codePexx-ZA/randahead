using BudgetApi.Controllers;
using BudgetApi.Data;
using BudgetApi.Models;
using BudgetApi.Services;
using Microsoft.EntityFrameworkCore;

namespace BudgetApi.Tests;

public class StatementFiguresTests
{
    private static readonly DateOnly Period = new(2025, 3, 1);

    [Fact]
    public async Task Targets_UseTheChosenBudget_NotAnEarlierOne()
    {
        await using var db = NewDb();
        AddBudget(db, "superseded", 8_000_000m, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var chosen = AddBudget(db, "active", 10_409_375m, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));
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
        var budget = await figures.ChosenBudgetAsync("unit", CancellationToken.None);
        Assert.NotNull(budget);
        var amounts = await figures.BudgetAsync(budget, CancellationToken.None);
        var targets = await figures.TargetsAsync(
            "unit",
            new SortedDictionary<DateOnly, Dictionary<string, decimal>>(),
            amounts,
            _ => "FY2026",
            CancellationToken.None
        );

        Assert.Equal(chosen.BudgetId, budget.BudgetId);
        var target = Assert.Single(targets);
        Assert.Equal(10_409_375m, target.Value);
        Assert.Equal("budget", target.Source);
        Assert.Equal("on_track", target.Status);
    }

    [Fact]
    public async Task ChosenBudget_IsMissing_WhenNoneIsActive()
    {
        await using var db = NewDb();
        AddBudget(db, "superseded", 10_725_000m, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        await db.SaveChangesAsync();

        Assert.Null(await new StatementFigures(db).ChosenBudgetAsync("unit", CancellationToken.None));
    }

    [Fact]
    public async Task ChoosingABudget_MakesItActiveAtOnce_AndRetiresThePreviousOne()
    {
        await using var db = NewDb();
        var previous = AddBudget(db, "active", 9_000_000m, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await db.SaveChangesAsync();

        var chosen = AddBudget(db, "superseded", 10_409_375m, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        BudgetsController.Activate([previous], chosen);
        await db.SaveChangesAsync();

        Assert.Equal("active", chosen.Status);
        Assert.Equal("superseded", previous.Status);
        var budget = await new StatementFigures(db).ChosenBudgetAsync("unit", CancellationToken.None);
        Assert.Equal(chosen.BudgetId, budget?.BudgetId);
        Assert.Single(await db.BudgetSelections.Where(b => b.Status == "active").ToListAsync());
    }

    [Fact]
    public async Task ActiveHistory_ReadsANewUploadAtOnce_AndIgnoresSupersededVersions()
    {
        await using var db = NewDb();
        AddUpload(db, "superseded", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 9_000_000m);
        AddUpload(db, "active", new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), 11_000_000m);
        await db.SaveChangesAsync();

        var history = await new StatementFigures(db).ActiveHistoryAsync("unit", CancellationToken.None);

        Assert.Equal(11_000_000m, Assert.Single(history).Value[IncomeStatement.Revenue]);
    }

    [Fact]
    public void SavingAnUpload_KeepsItActive_AndSupersedesFullyCoveredOlderVersions()
    {
        var covered = Upload("active", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 2023, 2024);
        var partly = Upload("active", new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), 2022, 2023);
        var saved = Upload("active", new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), 2023, 2024, 2025);

        SubmissionsController.SupersedeCovered([saved, partly, covered]);

        Assert.Equal("active", saved.Status);
        Assert.Equal("active", partly.Status);
        Assert.Equal("superseded", covered.Status);
    }

    private static BudgetDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<BudgetDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new BudgetDbContext(options);
        db.AppUsers.Add(new AppUser { UserId = "chooser", UnitId = "unit", FullName = "Demo User", Username = "demo123", PasswordHash = "hash" });
        return db;
    }

    private static BudgetSubmission Upload(string status, DateTime uploadedAt, params int[] years)
    {
        var upload = new BudgetSubmission { UnitId = "unit", Status = status, SubmittedBy = "user", SubmittedAt = uploadedAt };
        foreach (var year in years)
        {
            upload.LineItems.Add(new FinancialLineItem { Category = IncomeStatement.Revenue, PeriodStart = new DateOnly(year, 3, 1), Amount = 1m });
        }
        return upload;
    }

    private static void AddUpload(BudgetDbContext db, string status, DateTime uploadedAt, decimal revenue)
    {
        var upload = new BudgetSubmission { UnitId = "unit", Status = status, SubmittedBy = "user", SubmittedAt = uploadedAt };
        db.BudgetSubmissions.Add(upload);
        db.FinancialLineItems.Add(new FinancialLineItem
        {
            SubmissionId = upload.SubmissionId,
            Category = IncomeStatement.Revenue,
            PeriodStart = Period,
            Amount = revenue,
        });
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
