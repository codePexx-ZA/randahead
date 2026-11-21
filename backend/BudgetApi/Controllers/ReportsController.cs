using BudgetApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BudgetApi.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize]
public class ReportsController(StatementFigures figures) : ApiController
{
    private static readonly (string Code, string Name)[] Ratios =
    [
        ("gross_margin", "Gross margin"),
        ("operating_margin", "Operating margin"),
        ("net_margin", "Net margin"),
    ];

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var caller = Caller;
        var unit = caller.Unit!;
        string Label(DateOnly start) => FinancialYear.Label(start, unit.Business!.YearEndMonth);

        var history = await figures.ActiveHistoryAsync(caller.UnitId, cancellationToken);
        var chosen = await figures.ChosenBudgetAsync(caller.UnitId, cancellationToken);
        var budget = chosen is null ? null : await figures.BudgetAsync(chosen, cancellationToken);

        var lastActual = history.Count > 0 ? history.Keys.Last() : DateOnly.MinValue;
        var columns = history.Select(h => (Period: h.Key, Source: "actual", Figures: h.Value)).ToList();
        if (budget is not null)
        {
            columns.AddRange(budget.Where(b => b.Key > lastActual).Select(b => (Period: b.Key, Source: "budget", Figures: b.Value)));
        }
        var first = history.Count > 1 ? history.First().Value : null;
        var last = history.Count > 1 ? history.Last().Value : null;

        var lines = (await figures.LayoutAsync(cancellationToken)).Select(line => new
        {
            line.Code,
            line.Name,
            line.Kind,
            line.TeamManaged,
            line.Subtotal,
            values = columns.Select(c => c.Figures.GetValueOrDefault(line.Code)),
            change = first is null ? null : StatementFigures.PercentChange(last!.GetValueOrDefault(line.Code), first.GetValueOrDefault(line.Code)),
        });

        var revenue = columns.Select(c => (decimal?)c.Figures.GetValueOrDefault(IncomeStatement.Revenue)).ToList();
        var ratios = new[]
        {
            new
            {
                code = "revenue_growth",
                name = "Revenue growth",
                values = revenue.Select((value, i) => i == 0 ? null : StatementFigures.PercentChange(value, revenue[i - 1])).ToList(),
            },
        }.Concat(Ratios.Select(ratio => new
        {
            code = ratio.Code,
            name = ratio.Name,
            values = columns
                .Select(c => IncomeStatement.Kpi(ratio.Code, c.Figures) is { } value ? Math.Round(value, 1) : (decimal?)null)
                .ToList(),
        }));

        return Ok(new
        {
            business = unit.Business!.LegalName,
            unit = unit.UnitName,
            generatedOn = DisplayText.Day(DateTime.UtcNow),
            columns = columns.Select(c => new { year = Label(c.Period), source = c.Source }),
            changeFrom = first is null ? null : Label(history.Keys.First()),
            changeTo = first is null ? null : Label(lastActual),
            budget = chosen is null ? null : StatementFigures.ChosenBudgetSummary(chosen),
            lines = columns.Count == 0 ? [] : lines,
            ratios = columns.Count == 0 ? [] : ratios,
            targets = await figures.TargetsAsync(caller.UnitId, history, budget, Label, cancellationToken),
        });
    }
}
