// Budgets API: the decision maker's choice of forecast as the budget and the approver's decision on it
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
    string? Decision,
    string? DecidedBy,
    string? DecidedOn,
    string? Comment,
    string? ApprovedBy,
    string? ApprovedOn,
    bool OutOfDate,
    IReadOnlyList<ForecastRunView> Runs
);

[ApiController]
[Route("api/budgets")]
[Authorize]
public class BudgetsController(BudgetDbContext db, BusinessScope scope, ForecastRuns runs, AuditLog audit) : ControllerBase
{
    private static readonly HashSet<string> Methods = [ForecastRuns.MovingAverage, ForecastRuns.ExponentialSmoothing, "combined"];

    [HttpGet("latest")]
    public async Task<IActionResult> Latest(CancellationToken cancellationToken)
    {
        if (await scope.CallerAsync(User, cancellationToken) is not { } caller)
        {
            return SessionEnded();
        }
        var latest = await Budgets(caller.UnitId)
            .AsNoTracking()
            .Where(b => b.Status != "superseded")
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
        if (await scope.CallerAsync(User, cancellationToken) is not { } caller)
        {
            return SessionEnded();
        }
        if (caller.Role != "decision_maker" && !caller.IsAdmin)
        {
            return Forbidden("Only a Decision Maker can choose the budget.");
        }
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
            .Select(r => new { r.RunId, r.ForecastingMethod, r.CreatedAt, r.HistoryEnd })
            .ToListAsync(cancellationToken);
        if (pair.Count != 2)
        {
            return NotFound(new { message = "These forecasts no longer exist. Run the forecasts again." });
        }
        if (!ForecastRuns.IsPair(pair.Select(r => r.ForecastingMethod)) || pair[0].CreatedAt != pair[1].CreatedAt)
        {
            return BadRequest(new { message = "Both forecasts must come from the same run." });
        }
        if (pair[0].HistoryEnd != await runs.LatestApprovedPeriodAsync(caller.UnitId, cancellationToken))
        {
            return Conflict(new { message = "The approved history has changed since these forecasts ran. Run the forecasts again first." });
        }

        var maRunId = pair.Single(r => r.ForecastingMethod == ForecastRuns.MovingAverage).RunId;
        var esRunId = pair.Single(r => r.ForecastingMethod == ForecastRuns.ExponentialSmoothing).RunId;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var open = await Budgets(caller.UnitId).Where(b => b.Status == "submitted" || b.Status == "approved").ToListAsync(cancellationToken);
        if (open.Any(b => b.MaRunId == maRunId && b.BudgetMethod == request.Method))
        {
            return Conflict(new { message = "This option is already your budget." });
        }

        foreach (var waiting in open.Where(b => b.Status == "submitted"))
        {
            waiting.Status = "superseded";
        }
        await db.SaveChangesAsync(cancellationToken);
        var budget = new BudgetSelection
        {
            UnitId = caller.UnitId,
            MaRunId = maRunId,
            EsRunId = esRunId,
            BudgetMethod = request.Method,
            ChosenBy = caller.UserId,
            Chooser = caller,
        };
        db.BudgetSelections.Add(budget);
        audit.Add(caller, "budget", "chosen", $"Chose {AuditLog.MethodNames[request.Method]} as the budget");
        if (await SaveAsync(transaction, cancellationToken) is { } clash)
        {
            return clash;
        }
        return Ok(await ViewAsync(budget, cancellationToken));
    }

    [HttpPost("{id}/decision")]
    public async Task<IActionResult> Decide(string id, DecisionRequest request, CancellationToken cancellationToken)
    {
        if (await scope.CallerAsync(User, cancellationToken) is not { } caller)
        {
            return SessionEnded();
        }
        if (caller.Role != "approver" && !caller.IsAdmin)
        {
            return Forbidden("Only an Approver can approve or reject budgets.");
        }

        var comments = string.IsNullOrWhiteSpace(request.Comments) ? null : request.Comments.Trim();
        var problem = (request.Decision, comments) switch
        {
            (not ("approved" or "rejected"), _) => "The decision must be approved or rejected.",
            ("rejected", null) => "Add a comment so the decision maker knows what to change.",
            (_, { Length: > 2000 }) => "The comment can be at most 2000 characters.",
            _ => null,
        };
        if (problem is not null)
        {
            return BadRequest(new { message = problem });
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var budgets = await Budgets(caller.UnitId).Where(b => b.BudgetId == id || b.Status == "approved").ToListAsync(cancellationToken);
        if (budgets.FirstOrDefault(b => b.BudgetId == id) is not { } target)
        {
            return NotFound(new { message = "This budget no longer exists." });
        }
        if (target.Status != "submitted")
        {
            return Conflict(new { message = "This budget has already been decided." });
        }

        if (request.Decision == "approved")
        {
            foreach (var previous in budgets.Where(b => b.Status == "approved"))
            {
                previous.Status = "superseded";
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        target.Status = request.Decision!;
        target.Approvals.Add(
            new ApprovalAction
            {
                BudgetId = target.BudgetId,
                ActedBy = caller.UserId,
                Actor = caller,
                Decision = request.Decision!,
                Comments = comments,
            }
        );
        audit.Add(
            caller,
            "budget",
            target.Status,
            $"{(target.Status == "approved" ? "Approved" : "Rejected")} the budget ({AuditLog.MethodNames[target.BudgetMethod]})",
            comments is null ? null : [new AuditChange("Comment", null, comments)]
        );
        if (await SaveAsync(transaction, cancellationToken) is { } clash)
        {
            return clash;
        }
        return Ok(await ViewAsync(target, cancellationToken));
    }

    private IQueryable<BudgetSelection> Budgets(string unitId) =>
        db
            .BudgetSelections.Where(b => b.UnitId == unitId)
            .Include(b => b.Chooser)
            .Include(b => b.Approvals)
            .ThenInclude(a => a.Actor);

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
        var decision = b.Approvals.OrderByDescending(a => a.ActedAt).FirstOrDefault();
        var approved = b.Status is "approved" or "superseded" && decision?.Decision == "approved";
        var budgetRuns = await runs.LoadAsync(b.UnitId, [b.MaRunId, b.EsRunId], cancellationToken);
        var latestPeriod = await runs.LatestApprovedPeriodAsync(b.UnitId, cancellationToken);
        return new BudgetView(
            b.BudgetId,
            b.BudgetMethod,
            b.Status,
            [b.MaRunId, b.EsRunId],
            DisplayText.Person(b.Chooser),
            DisplayText.Day(b.ChosenAt),
            decision?.Decision,
            decision is null ? null : DisplayText.Person(decision.Actor),
            DisplayText.Day(decision?.ActedAt),
            decision?.Comments,
            approved ? DisplayText.Person(decision!.Actor) : null,
            approved ? DisplayText.Day(decision!.ActedAt) : null,
            budgetRuns.Any(r => r.HistoryEnd != latestPeriod),
            budgetRuns
        );
    }

    private ObjectResult Forbidden(string message) => StatusCode(StatusCodes.Status403Forbidden, new { message });

    private UnauthorizedObjectResult SessionEnded() => Unauthorized(new { message = "Your session has ended. Sign in again." });
}
