// Statement API: the income statement's categories, subtotals, display layout, KPIs and which categories each economic indicator affects
using BudgetApi.Data;
using BudgetApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BudgetApi.Controllers;

[ApiController]
[Route("api/statement")]
[Authorize]
public class StatementController(BudgetDbContext db, StatementFigures figures) : ApiController
{
    [HttpGet("definitions")]
    [RequirePermission(Permissions.ViewStatement)]
    public async Task<IActionResult> Definitions(CancellationToken cancellationToken)
    {
        var categories = await figures.CategoriesAsync(cancellationToken);
        var links = await db.IndicatorCategoryLinks.AsNoTracking().ToListAsync(cancellationToken);
        return Ok(new
        {
            categories = categories.Select(c => new
            {
                code = c.CategoryCode,
                name = c.DisplayName,
                kind = IncomeStatement.IsIncome(c.CategoryCode) ? "income" : "expense",
                teamManaged = c.TeamManaged,
            }),
            subtotals = IncomeStatement.Subtotals.Select(s => new { code = s.Code, name = s.Name }),
            layout = IncomeStatement.Layout,
            kpis = IncomeStatement.Kpis(categories.ToDictionary(c => c.CategoryCode, c => c.DisplayName)),
            indicators = links
                .GroupBy(l => l.IndicatorCode)
                .ToDictionary(g => g.Key, g => g.Select(l => l.CategoryCode).OrderBy(code => Array.IndexOf(IncomeStatement.Categories, code))),
        });
    }
}
