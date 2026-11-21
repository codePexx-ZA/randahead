using System.Text.Json.Serialization;
using BudgetApi.Data;
using BudgetApi.Models;
using BudgetApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BudgetApi.Controllers;

public record BudgetRequest(
    [property: JsonPropertyName("method")] string? Method,
    [property: JsonPropertyName("run_ids")] string[]? RunIds
);

public record BudgetView(
    string Id,
    string Method,
    string Status,
    string[] RunIds,
    string ChosenBy,
    string? ChosenOn,
    bool OutOfDate,
    IReadOnlyList<ForecastRunView> Runs,
    ForecastComparison Comparison
);

[ApiController]
[Route("api/budgets")]
[Authorize]
public class BudgetsController(BudgetDbContext db, BusinessScope scope, ForecastRuns runs, AuditLog audit) : ApiController
{
    private static readonly HashSet<string> Methods = [ForecastRuns.MovingAverage, ForecastRuns.ExponentialSmoothing, "combined"];

    [HttpGet("latest")]
    public async Task<IActionResult> Latest(CancellationToken cancellationToken)
    {
        var caller = Caller;
        var latest = await Budgets(caller.UnitId)
            .AsNoTracking()
            .Where(b => b.Status == "active")
            .OrderByDescending(b => b.ChosenAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (latest is null)
        {
            return NotFound(new { message = "No budget has been chosen yet." });
        }
        return Ok(await ViewAsync(latest, cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> Choose(BudgetRequest request, CancellationToken cancellationToken)
    {
        var caller = Caller;
        if (request.Method is null || !Methods.Contains(request.Method))
        {
            return BadRequest(new { message = "Choose Moving Average, Exponential Smoothing or both (average)." });
        }
        if (request.RunIds is not { Length: 2 } runIds || runIds[0] == runIds[1])
        {
            return BadRequest(new { message = "Send the Moving Average and Exponential Smoothing runs to choose from." });
        }

        var pair = await db
            .ForecastRuns.AsNoTracking()
            .Where(r => r.UnitId == caller.UnitId && runIds.Contains(r.RunId))
            .Select(r => new { r.RunId, r.ForecastingMethod, r.CreatedAt })
            .ToListAsync(cancellationToken);
        if (pair.Count != 2)
        {
            return NotFound(new { message = "These forecasts no longer exist. Run the forecasts again." });
        }
        if (!ForecastRuns.IsPair(pair.Select(r => r.ForecastingMethod)) || pair[0].CreatedAt != pair[1].CreatedAt)
        {
            return BadRequest(new { message = "Both forecasts must come from the same run." });
        }

        var maRunId = pair.Single(r => r.ForecastingMethod == ForecastRuns.MovingAverage).RunId;
        var esRunId = pair.Single(r => r.ForecastingMethod == ForecastRuns.ExponentialSmoothing).RunId;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await scope.LockUnitAsync(caller.UnitId, cancellationToken);
        if (!await runs.MatchesHistoryAsync(caller.UnitId, [maRunId, esRunId], cancellationToken))
        {
            return Conflict(new { message = "Your income statement history has changed since these forecasts ran. Run the forecasts again first." });
        }
        var current = await Budgets(caller.UnitId).Where(b => b.Status == "active").ToListAsync(cancellationToken);
        if (current.Any(b => b.MaRunId == maRunId && b.BudgetMethod == request.Method))
        {
            return Conflict(new { message = "This option is already your budget." });
        }

        var budget = new BudgetSelection
        {
            UnitId = caller.UnitId,
            MaRunId = maRunId,
            EsRunId = esRunId,
            BudgetMethod = request.Method,
            ChosenBy = caller.UserId,
            Chooser = caller,
        };
        Activate(current, budget);
        await db.SaveChangesAsync(cancellationToken);
        db.BudgetSelections.Add(budget);
        audit.Add(caller, "budget", "chosen", $"Chose {AuditLog.MethodNames[request.Method]} as the budget");
        if (await SaveAsync(transaction, cancellationToken) is { } clash)
        {
            return clash;
        }
        return Ok(await ViewAsync(budget, cancellationToken));
    }

    internal static void Activate(IEnumerable<BudgetSelection> current, BudgetSelection chosen)
    {
        foreach (var previous in current)
        {
            previous.Status = "superseded";
        }
        chosen.Status = "active";
    }

    private IQueryable<BudgetSelection> Budgets(string unitId) =>
        db
            .BudgetSelections.Where(b => b.UnitId == unitId)
            .Include(b => b.Chooser);

    private async Task<ObjectResult?> SaveAsync(IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateException)
        {
            return Conflict(new { message = "Someone else changed the budget at the same time. Reload and try again." });
        }
    }

    private async Task<BudgetView> ViewAsync(BudgetSelection b, CancellationToken cancellationToken)
    {
        var (budgetRuns, comparison, matchesHistory) = await runs.LoadPairAsync(
            b.UnitId,
            [b.MaRunId, b.EsRunId],
            Caller.Unit!.Business!.YearEndMonth,
            cancellationToken
        );
        return new BudgetView(
            b.BudgetId,
            b.BudgetMethod,
            b.Status,
            [b.MaRunId, b.EsRunId],
            DisplayText.Person(b.Chooser),
            DisplayText.Day(b.ChosenAt),
            !matchesHistory,
            budgetRuns,
            comparison
        );
    }
}
