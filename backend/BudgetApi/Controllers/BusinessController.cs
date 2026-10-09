// Business API: shows and changes the signed-in user's business details
using System.Globalization;
using System.Text.Json.Serialization;
using BudgetApi.Data;
using BudgetApi.Models;
using BudgetApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BudgetApi.Controllers;

public record BusinessRequest(
    [property: JsonPropertyName("legal_name")] string? LegalName,
    [property: JsonPropertyName("industry")] string? Industry,
    [property: JsonPropertyName("year_end_month")] int? YearEndMonth
);

[ApiController]
[Route("api/business")]
[Authorize]
public class BusinessController(BudgetDbContext db, BusinessScope scope, AuditLog audit) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        if (await scope.CallerAsync(User, cancellationToken) is not { } caller)
        {
            return SessionEnded();
        }
        var business = caller.Unit!.Business!;
        return Ok(BusinessView(caller, await scope.HasApprovedHistoryAsync(business.BusinessId, cancellationToken)));
    }

    [HttpPut]
    [Authorize(Roles = "decision_maker")]
    public async Task<IActionResult> Update(BusinessRequest request, CancellationToken cancellationToken)
    {
        if (await scope.CallerAsync(User, cancellationToken) is not { } caller)
        {
            return SessionEnded();
        }

        var name = request.LegalName?.Trim() ?? "";
        var industry = AccountRules.CleanIndustry(request.Industry);
        if (AccountRules.BusinessProblem(name, industry, request.YearEndMonth) is { } problem)
        {
            return BadRequest(new { message = problem });
        }

        var business = caller.Unit!.Business!;
        var hasHistory = await scope.HasApprovedHistoryAsync(business.BusinessId, cancellationToken);
        if (hasHistory && request.YearEndMonth != business.YearEndMonth)
        {
            return Conflict(new { message = "The financial year-end can't change once income statement history is approved." });
        }

        var changes = AuditLog
            .Changed(
                new AuditChange("Name", business.LegalName, name),
                new AuditChange("Industry", business.Industry ?? "Not set", industry ?? "Not set"),
                new AuditChange("Year-end month", MonthName(business.YearEndMonth), MonthName(request.YearEndMonth!.Value))
            )
            .ToList();
        business.LegalName = name;
        business.Industry = industry;
        business.YearEndMonth = (short)request.YearEndMonth!.Value;
        if (changes.Count > 0)
        {
            audit.Add(caller, "business", "changed", "Changed the business details", changes);
        }
        await db.SaveChangesAsync(cancellationToken);
        return Ok(BusinessView(caller, hasHistory));
    }

    private UnauthorizedObjectResult SessionEnded() => Unauthorized(new { message = "Your session has ended. Sign in again." });

    private static string MonthName(int month) => CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month);

    private static object BusinessView(AppUser caller, bool hasApprovedHistory)
    {
        var unit = caller.Unit!;
        var business = unit.Business!;
        return new
        {
            name = business.LegalName,
            business.Industry,
            business.Country,
            business.YearEndMonth,
            unit.UnitId,
            unit.UnitName,
            unit.Currency,
            hasApprovedHistory,
        };
    }
}
