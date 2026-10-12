// Dashboard API: one financial year's figures against the year before and its approved budget, plus KPI target progress
using BudgetApi.Data;
using BudgetApi.Models;
using BudgetApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BudgetApi.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController(BudgetDbContext db, StatementFigures figures) : ApiController
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? year, CancellationToken cancellationToken)
    {
        var caller = Caller;
        var yearEndMonth = caller.Unit!.Business!.YearEndMonth;
        string Label(DateOnly start) => FinancialYear.Label(start, yearEndMonth);

        var history = await figures.ApprovedHistoryAsync(caller.UnitId, cancellationToken);
        var periods = history.Keys.ToList();
        var index = year is null ? periods.Count - 1 : periods.FindIndex(p => Label(p) == year);
        if (year is not null && index < 0)
        {
            return BadRequest(new
            {
                message = periods.Count == 0
                    ? "There's no approved income statement history yet."
                    : $"Choose a year in the approved history ({Label(periods[0])}–{Label(periods[^1])}).",
            });
        }

        var period = index >= 0 ? periods[index] : (DateOnly?)null;
        var yearBudget = period is null ? null : await ApprovedBudgetForAsync(caller.UnitId, period.Value, cancellationToken);
        var yearBudgetFigures = yearBudget is null ? null : (await figures.BudgetAsync(yearBudget, cancellationToken))[period!.Value];

        var actual = period is null ? null : history[period.Value];
        var previous = index > 0 ? history[periods[index - 1]] : null;
        List<object> lines = period is null
            ? []
            : (await figures.LayoutAsync(cancellationToken)).Select(line =>
            {
                var now = actual!.GetValueOrDefault(line.Code);
                decimal? before = previous?.GetValueOrDefault(line.Code);
                decimal? budget = yearBudgetFigures?.GetValueOrDefault(line.Code);
                return new
                {
                    line.Code,
                    line.Name,
                    line.Kind,
                    line.TeamManaged,
                    line.Subtotal,
                    actual = now,
                    previous = before,
                    change = StatementFigures.PercentChange(now, before),
                    budget,
                    variance = now - budget,
                    variancePercent = StatementFigures.PercentChange(now, budget),
                };
            }).ToList<object>();

        var latestFigures = await figures.LatestBudgetAsync(caller.UnitId, cancellationToken);
        return Ok(new
        {
            years = periods.Select(Label),
            year = period is null ? null : Label(period.Value),
            previousYear = index > 0 ? Label(periods[index - 1]) : null,
            budget = yearBudget is null ? null : StatementFigures.ApprovedBudgetSummary(yearBudget),
            lines,
            targets = await figures.TargetsAsync(caller.UnitId, history, latestFigures, Label, cancellationToken),
        });
    }

    private async Task<BudgetSelection?> ApprovedBudgetForAsync(string unitId, DateOnly period, CancellationToken cancellationToken) =>
        (
            await db
                .BudgetSelections.AsNoTracking()
                .Where(b =>
                    b.UnitId == unitId
                    && (b.Status == "approved" || b.Status == "superseded")
                    && b.Approvals.Any(a => a.Decision == "approved")
                    && db.ForecastValues.Any(v => v.RunId == b.MaRunId && v.ForecastPeriod == period)
                )
                .Include(b => b.Approvals)
                .ThenInclude(a => a.Actor)
                .ToListAsync(cancellationToken)
        )
            .OrderByDescending(b => b.Approvals.Where(a => a.Decision == "approved").Max(a => a.ActedAt))
            .FirstOrDefault();
}
