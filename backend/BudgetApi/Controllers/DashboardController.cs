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

        var history = await figures.ActiveHistoryAsync(caller.UnitId, cancellationToken);
        var periods = history.Keys.ToList();
        var index = year is null ? periods.Count - 1 : periods.FindIndex(p => Label(p) == year);
        if (year is not null && index < 0)
        {
            return BadRequest(new
            {
                message = periods.Count == 0
                    ? "There's no income statement history yet."
                    : $"Choose a year in your history ({Label(periods[0])}–{Label(periods[^1])}).",
            });
        }

        var period = index >= 0 ? periods[index] : (DateOnly?)null;
        var yearBudget = period is null ? null : await ChosenBudgetForAsync(caller.UnitId, period.Value, cancellationToken);
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

        var chosen = await figures.ChosenBudgetAsync(caller.UnitId, cancellationToken);
        var chosenFigures = chosen is null ? null : await figures.BudgetAsync(chosen, cancellationToken);
        return Ok(new
        {
            years = periods.Select(Label),
            year = period is null ? null : Label(period.Value),
            previousYear = index > 0 ? Label(periods[index - 1]) : null,
            budget = yearBudget is null ? null : StatementFigures.ChosenBudgetSummary(yearBudget),
            lines,
            targets = await figures.TargetsAsync(caller.UnitId, history, chosenFigures, Label, cancellationToken),
        });
    }

    private Task<BudgetSelection?> ChosenBudgetForAsync(string unitId, DateOnly period, CancellationToken cancellationToken) =>
        db
            .BudgetSelections.AsNoTracking()
            .Where(b => b.UnitId == unitId && db.ForecastValues.Any(v => v.RunId == b.MaRunId && v.ForecastPeriod == period))
            .Include(b => b.Chooser)
            .OrderByDescending(b => b.ChosenAt)
            .FirstOrDefaultAsync(cancellationToken);
}
