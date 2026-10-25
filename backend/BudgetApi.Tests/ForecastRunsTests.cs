// Forecast run tests: a correction to an earlier year makes a forecast stale even when the latest year stays
using BudgetApi.Data;
using BudgetApi.Models;
using BudgetApi.Services;
using Microsoft.EntityFrameworkCore;

namespace BudgetApi.Tests;

public class ForecastRunsTests
{
    private static readonly DateTime FirstApproval = new(2026, 9, 28, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime CorrectionApproval = new(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task EarlierYearCorrection_MakesTheForecastStale()
    {
        await using var db = NewDb();
        var original = AddSubmission(db, "approved", FirstApproval);
        AddLine(db, original, new DateOnly(2022, 3, 1), 1_000m);
        AddLine(db, original, new DateOnly(2024, 3, 1), 2_000m);
        var ma = AddRun(db, ForecastRuns.MovingAverage, FirstApproval);
        var es = AddRun(db, ForecastRuns.ExponentialSmoothing, FirstApproval);
        await db.SaveChangesAsync();

        var runs = new ForecastRuns(db, new StatementFigures(db), new ForecastClient(new HttpClient()));
        Assert.Equal(new DateOnly(2024, 3, 1), await LatestPeriod(db));
        Assert.True(await runs.MatchesHistoryAsync("unit", [ma, es], CancellationToken.None));

        var correction = AddSubmission(db, "approved", CorrectionApproval);
        AddLine(db, correction, new DateOnly(2022, 3, 1), 2_000m);
        await db.SaveChangesAsync();

        Assert.Equal(new DateOnly(2024, 3, 1), await LatestPeriod(db));
        Assert.False(await runs.MatchesHistoryAsync("unit", [ma, es], CancellationToken.None));
    }

    private static BudgetDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<BudgetDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new BudgetDbContext(options);
    }

    private static string AddSubmission(BudgetDbContext db, string status, DateTime approvedAt)
    {
        var id = Guid.NewGuid().ToString("N");
        db.BudgetSubmissions.Add(new BudgetSubmission { SubmissionId = id, UnitId = "unit", Status = status, SubmittedBy = "submitter" });
        db.ApprovalActions.Add(new ApprovalAction
        {
            SubmissionId = id,
            ActedBy = "approver",
            Decision = "approved",
            ActedAt = approvedAt,
        });
        return id;
    }

    private static void AddLine(BudgetDbContext db, string submissionId, DateOnly period, decimal amount) =>
        db.FinancialLineItems.Add(new FinancialLineItem { SubmissionId = submissionId, Category = "revenue", PeriodStart = period, Amount = amount });

    private static string AddRun(BudgetDbContext db, string method, DateTime approvedAt)
    {
        var id = Guid.NewGuid().ToString("N");
        db.ForecastRuns.Add(new ForecastRun
        {
            RunId = id,
            UnitId = "unit",
            ForecastingMethod = method,
            HistoryStart = new DateOnly(2022, 3, 1),
            HistoryEnd = new DateOnly(2024, 3, 1),
            HistoryApprovedAt = approvedAt,
            CreatedAt = FirstApproval,
        });
        return id;
    }

    private static Task<DateOnly> LatestPeriod(BudgetDbContext db) =>
        db.FinancialLineItems.MaxAsync(i => i.PeriodStart);
}
