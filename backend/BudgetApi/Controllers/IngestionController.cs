// Ingestion API: fetches and saves external indicators, and returns the new alerts for the caller's business only
using BudgetApi.Data;
using BudgetApi.Models;
using BudgetApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace BudgetApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequirePermission(Permissions.RunIngestion)]
[EnableRateLimiting(RateLimits.Ingestion)]
public class IngestionController(IngestionService ingestionService, BudgetDbContext db) : ApiController
{
    [HttpPost("run")]
    public async Task<IActionResult> IngestAll(CancellationToken cancellationToken)
    {
        var caller = Caller;
        var results = await ingestionService.IngestAllAsync(cancellationToken);
        return Ok(await OwnAlertsOnlyAsync(results, caller, cancellationToken));
    }

    [HttpPost("{code}")]
    public async Task<IActionResult> IngestOne(string code, CancellationToken cancellationToken)
    {
        var caller = Caller;
        if (IndicatorCatalog.Find(code) is not { } definition)
        {
            return NotFound(
                new
                {
                    error = $"Unknown indicator '{code}'.",
                    validCodes = IndicatorCatalog.All.Select(d => d.Code),
                }
            );
        }
        var result = await ingestionService.IngestIndicatorAsync(definition, cancellationToken);
        return Ok((await OwnAlertsOnlyAsync([result], caller, cancellationToken))[0]);
    }

    private async Task<List<IngestResult>> OwnAlertsOnlyAsync(
        IReadOnlyList<IngestResult> results,
        AppUser caller,
        CancellationToken cancellationToken
    )
    {
        var ruleIds = (
            await db
                .RuleDefinitions.Where(r => r.BusinessId == caller.Unit!.BusinessId)
                .Select(r => r.RuleId)
                .ToListAsync(cancellationToken)
        ).ToHashSet();
        return results
            .Select(r =>
            {
                var alerts = r.Alerts.Where(a => ruleIds.Contains(a.RuleId)).ToList();
                return r with { Alerts = alerts, AlertsCreated = alerts.Count };
            })
            .ToList();
    }
}
