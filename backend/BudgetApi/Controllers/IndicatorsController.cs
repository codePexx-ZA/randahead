// Indicators API: returns indicators, categories and alerts
using BudgetApi.Data;
using BudgetApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BudgetApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class IndicatorsController(BudgetDbContext db, BusinessScope scope) : ControllerBase
{
    [HttpGet("series")]
    public async Task<IActionResult> GetSeries(CancellationToken cancellationToken)
    {
        var values = await db
            .ExternalIndicators.AsNoTracking()
            .Where(i => i.ValidationStatus == "validated")
            .OrderBy(i => i.RelevantPeriod)
            .Select(i => new { i.IndicatorCode, i.RelevantPeriod, i.IndicatorValue })
            .ToListAsync(cancellationToken);

        var series = values
            .GroupBy(i => i.IndicatorCode)
            .ToDictionary(g => g.Key, g => g.ToDictionary(i => i.RelevantPeriod, i => Math.Round(i.IndicatorValue, 2)));
        return Ok(series);
    }

    [HttpGet]
    public async Task<IActionResult> GetLatest(
        [FromQuery] string? code = null,
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default
    )
    {
        take = Math.Clamp(take, 1, 100);

        var query = db.ExternalIndicators.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(code))
        {
            query = query.Where(i => i.IndicatorCode == code);
        }

        var items = await query
            .OrderByDescending(i => i.RelevantPeriod)
            .ThenBy(i => i.IndicatorCode)
            .Take(take)
            .Select(i => new
            {
                i.IndicatorId,
                i.SourceId,
                i.IndicatorCode,
                i.IndicatorName,
                i.IndicatorValue,
                i.MeasurementUnit,
                i.RelevantPeriod,
                i.ValidationStatus,
                i.CollectedAt,
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("catalog")]
    public async Task<IActionResult> GetCatalog(CancellationToken cancellationToken)
    {
        if (await scope.CallerAsync(User, cancellationToken) is not { } caller)
        {
            return Unauthorized(new { message = "Your session has ended. Sign in again." });
        }
        var businessId = caller.Unit!.BusinessId;

        var types = await db
            .IndicatorTypes.AsNoTracking()
            .Select(t => new
            {
                t.IndicatorCode,
                t.DisplayName,
                t.Description,
                t.MeasurementUnit,
                t.OfficialSource,
                Categories = t.CategoryLinks.Select(l => l.Category!.DisplayName).ToList(),
            })
            .ToListAsync(cancellationToken);

        var thresholds = await db
            .RuleDefinitions.AsNoTracking()
            .Where(r => r.BusinessId == businessId && r.IndicatorCode != null && r.ActiveStatus == "active")
            .Select(r => new { r.IndicatorCode, r.ThresholdValue })
            .ToListAsync(cancellationToken);

        var catalog = IndicatorCatalog.All.Select(d =>
        {
            var type = types.FirstOrDefault(t => t.IndicatorCode == d.Code);
            return new
            {
                d.Code,
                type?.DisplayName,
                type?.Description,
                type?.MeasurementUnit,
                Provider = d.Provider.ToString(),
                d.SeriesKey,
                type?.OfficialSource,
                Threshold = thresholds.FirstOrDefault(r => r.IndicatorCode == d.Code)?.ThresholdValue,
                LinkedCategories = type?.Categories ?? [],
            };
        });

        return Ok(catalog);
    }

    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories(CancellationToken cancellationToken) =>
        Ok(
            await db
                .LineCategories.AsNoTracking()
                .OrderBy(c => c.SortOrder)
                .ToListAsync(cancellationToken)
        );

    [HttpGet("alerts")]
    public async Task<IActionResult> GetAlerts(
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default
    )
    {
        if (await scope.CallerAsync(User, cancellationToken) is not { } caller)
        {
            return Unauthorized(new { message = "Your session has ended. Sign in again." });
        }
        take = Math.Clamp(take, 1, 100);

        var alerts = await db
            .RuleAlerts.AsNoTracking()
            .Where(a => a.Rule!.BusinessId == caller.Unit!.BusinessId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(take)
            .Select(a => new
            {
                a.AlertId,
                a.RuleId,
                a.Rule!.IndicatorCode,
                a.Severity,
                a.Message,
                a.ResolutionStatus,
                a.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        return Ok(alerts);
    }
}
