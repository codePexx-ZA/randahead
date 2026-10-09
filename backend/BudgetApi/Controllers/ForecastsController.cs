// Forecasts API: runs both forecasting methods through the Python service and returns the business's latest pair of runs
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
public class ForecastsController(BudgetDbContext db, BusinessScope scope, ForecastRuns runs, ForecastClient client, AuditLog audit)
    : ControllerBase
{
    [HttpGet("latest")]
    public async Task<IActionResult> Latest(CancellationToken cancellationToken)
    {
        if (await scope.CallerAsync(User, cancellationToken) is not { } caller)
        {
            return SessionEnded();
        }
        var ids = await runs.LatestPairAsync(caller.UnitId, cancellationToken);
        if (ids.Count == 0)
        {
            return NotFound(new { message = "No forecasts have been run yet." });
        }
        return Ok(await runs.LoadAsync(caller.UnitId, ids, cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> Run(ForecastRequest request, CancellationToken cancellationToken)
    {
        if (await scope.CallerAsync(User, cancellationToken) is not { } caller)
        {
            return SessionEnded();
        }
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
            var ids = await client.RunAsync(
                new ForecastServiceRequest(caller.UnitId, "annual", horizon, windowSize, roundedAlpha),
                cancellationToken
            );
            audit.Add(
                caller,
                "forecast",
                "ran",
                $"Ran the forecasts for {horizon} year{(horizon == 1 ? "" : "s")} ahead",
                [
                    new AuditChange("Moving Average window", null, $"{windowSize} years"),
                    new AuditChange("Smoothing weight α", null, roundedAlpha.ToString("0.##", CultureInfo.InvariantCulture)),
                ]
            );
            await db.SaveChangesAsync(cancellationToken);
            return Ok(await runs.LoadAsync(caller.UnitId, ids, cancellationToken));
        }
        catch (ForecastServiceException error)
        {
            return StatusCode(error.StatusCode, new { message = error.Message });
        }
    }

    private UnauthorizedObjectResult SessionEnded() => Unauthorized(new { message = "Your session has ended. Sign in again." });
}
