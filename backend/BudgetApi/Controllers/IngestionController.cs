// Ingestion API: fetches and saves external indicators
using BudgetApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BudgetApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "approver,decision_maker")]
public class IngestionController(IngestionService ingestionService) : ControllerBase
{
    [HttpPost("run")]
    public async Task<IActionResult> IngestAll(CancellationToken cancellationToken) =>
        Ok(await ingestionService.IngestAllAsync(cancellationToken));

    [HttpPost("{code}")]
    public async Task<IActionResult> IngestOne(string code, CancellationToken cancellationToken) =>
        IndicatorCatalog.Find(code) is { } definition
            ? Ok(await ingestionService.IngestIndicatorAsync(definition, cancellationToken))
            : NotFound(
                new
                {
                    error = $"Unknown indicator '{code}'.",
                    validCodes = IndicatorCatalog.All.Select(d => d.Code),
                }
            );
}
