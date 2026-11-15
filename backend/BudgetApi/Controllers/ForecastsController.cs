using System.Globalization;
using System.Text.Json.Serialization;
using BudgetApi.Data;
using BudgetApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BudgetApi.Controllers;

public record ForecastRequest(
    [property: JsonPropertyName("frequency")] string? Frequency,
    [property: JsonPropertyName("horizon")] int? Horizon,
    [property: JsonPropertyName("window_size")] int? WindowSize,
    [property: JsonPropertyName("alpha")] double? Alpha
);

[ApiController]
[Route("api/forecasts")]
[Authorize]
public class ForecastsController(BudgetDbContext db, ForecastRuns runs, AuditLog audit) : ApiController
{
    [HttpGet("latest")]
    public async Task<IActionResult> Latest(CancellationToken cancellationToken)
    {
        var caller = Caller;
        var ids = await runs.LatestPairAsync(caller.UnitId, cancellationToken);
        if (ids.Count == 0)
        {
            return NotFound(new { message = "No forecasts have been run yet." });
        }
        return Ok(await runs.LoadPairAsync(caller.UnitId, ids, caller.Unit!.Business!.YearEndMonth, cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> Run(ForecastRequest request, CancellationToken cancellationToken)
    {
        var caller = Caller;
        var horizon = request.Horizon ?? 1;
        var windowSize = request.WindowSize ?? 3;
        var alpha = request.Alpha ?? 0.5;
        var problem = true switch
        {
            _ when request.Frequency is not (null or "annual") => "Only yearly forecasts can be run for now.",
            _ when horizon is < 1 or > 3 => "Forecast 1 to 3 years ahead.",
            _ when windowSize is < 2 or > 3 => "The Moving Average window must be 2 or 3 years.",
            _ when alpha is < 0.1 or > 0.9 => "The smoothing weight α must be between 0.1 and 0.9.",
            _ => null,
        };
        if (problem is not null)
        {
            return BadRequest(new { message = problem });
        }

        try
        {
            var roundedAlpha = Math.Round(alpha, 2);
            var created = await runs.CreateAsync(caller.UnitId, horizon, windowSize, roundedAlpha, cancellationToken);
            var preset = $"{windowSize}:{roundedAlpha.ToString("0.0", CultureInfo.InvariantCulture)}" switch
            {
                "3:0.3" => "Steady",
                "3:0.5" => "Balanced",
                "2:0.7" => "Recent",
                _ => null,
            };
            var ahead = $"{horizon} year{(horizon == 1 ? "" : "s")} ahead";
            var details = new List<AuditChange>
            {
                new("Moving average", null, $"Last {windowSize} years, counted equally"),
                new("Smoothing weight", null, roundedAlpha.ToString("0.0", CultureInfo.InvariantCulture)),
            };
            if (preset is not null)
            {
                details.Insert(0, new AuditChange("Prediction", null, preset));
            }
            audit.Add(
                caller,
                "forecast",
                "ran",
                preset is null ? $"Ran the forecasts for {ahead}" : $"Ran the {preset} forecast for {ahead}",
                details
            );
            await db.SaveChangesAsync(cancellationToken);
            var ids = created.Select(r => r.RunId).ToList();
            return Ok(await runs.LoadPairAsync(caller.UnitId, ids, caller.Unit!.Business!.YearEndMonth, cancellationToken));
        }
        catch (ForecastServiceException error)
        {
            return StatusCode(error.StatusCode, new { message = error.Message });
        }
    }
}
