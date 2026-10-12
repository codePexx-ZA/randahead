// KPI targets API: lists the caller's unit's goals per KPI per financial year with their progress, and adds, changes and deletes them
using System.Text.Json.Serialization;
using BudgetApi.Data;
using BudgetApi.Models;
using BudgetApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BudgetApi.Controllers;

public record KpiTargetRequest(
    [property: JsonPropertyName("metric_name")] string? MetricName,
    [property: JsonPropertyName("target_value")] decimal? TargetValue,
    [property: JsonPropertyName("measurement_unit")] string? MeasurementUnit,
    [property: JsonPropertyName("target_period")] DateOnly? TargetPeriod
);

[ApiController]
[Route("api/kpi-targets")]
[Authorize]
public class KpiTargetsController(BudgetDbContext db, StatementFigures figures, AuditLog audit) : ApiController
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var caller = Caller;
        string Label(DateOnly start) => FinancialYear.Label(start, caller.Unit!.Business!.YearEndMonth);
        var history = await figures.ApprovedHistoryAsync(caller.UnitId, cancellationToken);
        var budget = await figures.LatestBudgetAsync(caller.UnitId, cancellationToken);
        return Ok(new
        {
            targets = await figures.TargetsAsync(caller.UnitId, history, budget, Label, cancellationToken),
            figures = StatementFigures.KpiFigures(history, budget, await figures.KpisAsync(cancellationToken), Label),
        });
    }

    [HttpPost]
    [RequirePermission(Permissions.EditTargets)]
    public async Task<IActionResult> Create(KpiTargetRequest request, CancellationToken cancellationToken)
    {
        var caller = Caller;
        if (await ProblemAsync(request, caller.Unit!.Business!.YearEndMonth, cancellationToken) is { } problem)
        {
            return BadRequest(new { message = problem });
        }
        if (await TakenAsync(caller.UnitId, request, null, cancellationToken))
        {
            return DuplicateConflict();
        }

        var target = new KpiTarget { UnitId = caller.UnitId };
        Apply(target, request);
        db.KpiTargets.Add(target);
        var yearEndMonth = caller.Unit!.Business!.YearEndMonth;
        audit.Add(
            caller,
            "target",
            "added",
            $"Added the {await KpiNameAsync(target.MetricName, cancellationToken)} target for {FinancialYear.Label(target.TargetPeriod, yearEndMonth)}",
            [new AuditChange("Target", null, ValueText(target))]
        );
        return await SaveAsync(cancellationToken) ?? Ok(TargetView(target));
    }

    [HttpPut("{id}")]
    [RequirePermission(Permissions.EditTargets)]
    public async Task<IActionResult> Update(string id, KpiTargetRequest request, CancellationToken cancellationToken)
    {
        var caller = Caller;
        if (await ProblemAsync(request, caller.Unit!.Business!.YearEndMonth, cancellationToken) is { } problem)
        {
            return BadRequest(new { message = problem });
        }
        if (await db.KpiTargets.FirstOrDefaultAsync(t => t.TargetId == id && t.UnitId == caller.UnitId, cancellationToken) is not { } target)
        {
            return TargetGone();
        }
        if (await TakenAsync(caller.UnitId, request, id, cancellationToken))
        {
            return DuplicateConflict();
        }

        var yearEndMonth = caller.Unit!.Business!.YearEndMonth;
        var before = (
            Kpi: await KpiNameAsync(target.MetricName, cancellationToken),
            Year: FinancialYear.Label(target.TargetPeriod, yearEndMonth),
            Value: ValueText(target)
        );
        Apply(target, request);
        var kpi = await KpiNameAsync(target.MetricName, cancellationToken);
        var year = FinancialYear.Label(target.TargetPeriod, yearEndMonth);
        var changes = AuditLog
            .Changed(
                new AuditChange("KPI", before.Kpi, kpi),
                new AuditChange("Year", before.Year, year),
                new AuditChange("Target", before.Value, ValueText(target))
            )
            .ToList();
        if (changes.Count > 0)
        {
            audit.Add(caller, "target", "changed", $"Changed the {kpi} target for {year}", changes);
        }
        return await SaveAsync(cancellationToken) ?? Ok(TargetView(target));
    }

    [HttpDelete("{id}")]
    [RequirePermission(Permissions.EditTargets)]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        var caller = Caller;
        if (await db.KpiTargets.FirstOrDefaultAsync(t => t.TargetId == id && t.UnitId == caller.UnitId, cancellationToken) is not { } target)
        {
            return TargetGone();
        }
        db.KpiTargets.Remove(target);
        var year = FinancialYear.Label(target.TargetPeriod, caller.Unit!.Business!.YearEndMonth);
        audit.Add(
            caller,
            "target",
            "deleted",
            $"Deleted the {await KpiNameAsync(target.MetricName, cancellationToken)} target for {year}",
            [new AuditChange("Target", ValueText(target), null)]
        );
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<KpiDefinition?> KpiAsync(string? metric, CancellationToken cancellationToken) =>
        (await figures.KpisAsync(cancellationToken)).FirstOrDefault(k => k.Code == metric);

    private async Task<string> KpiNameAsync(string metric, CancellationToken cancellationToken) =>
        (await KpiAsync(metric, cancellationToken))?.Name ?? metric;

    private static string ValueText(KpiTarget target) =>
        target.MeasurementUnit == "rand" ? AuditLog.Rand(target.TargetValue) : $"{AuditLog.Number(target.TargetValue)}%";

    private Task<bool> TakenAsync(string unitId, KpiTargetRequest request, string? ownId, CancellationToken cancellationToken) =>
        db.KpiTargets.AnyAsync(
            t => t.UnitId == unitId
                && t.MetricName == request.MetricName
                && t.TargetPeriod == request.TargetPeriod
                && t.TargetId != ownId,
            cancellationToken
        );

    private async Task<string?> ProblemAsync(KpiTargetRequest request, int yearEndMonth, CancellationToken cancellationToken)
    {
        var unit = (await KpiAsync(request.MetricName, cancellationToken))?.Unit;
        if (unit is null)
        {
            return "Choose one of the listed KPIs.";
        }
        if (request.MeasurementUnit != unit)
        {
            return $"This KPI is measured in {(unit == "rand" ? "rands" : "percent")}.";
        }
        if (request.TargetValue is not { } value)
        {
            return unit == "rand" ? "Type the target in rands, e.g. 1 200 000." : "Type the target as a percentage, e.g. 7.5.";
        }
        if (unit == "rand" && value < 0)
        {
            return "A rand target can't be negative.";
        }
        if (unit == "rand" && value >= 10_000_000_000_000_000m)
        {
            return "That target is too large.";
        }
        if (unit == "percent" && (value < -100 || value > 100))
        {
            return "A margin target must be between -100% and 100%.";
        }
        if (request.TargetPeriod is not { Day: 1 } period || period.Month != yearEndMonth % 12 + 1)
        {
            return "Choose the financial year for the target.";
        }
        return null;
    }

    private static void Apply(KpiTarget target, KpiTargetRequest request)
    {
        target.MetricName = request.MetricName!;
        target.TargetValue = request.MeasurementUnit == "rand"
            ? Math.Round(request.TargetValue!.Value, MidpointRounding.AwayFromZero)
            : Math.Round(request.TargetValue!.Value, 1, MidpointRounding.AwayFromZero);
        target.MeasurementUnit = request.MeasurementUnit!;
        target.TargetPeriod = request.TargetPeriod!.Value;
    }

    private async Task<IActionResult?> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return DuplicateConflict();
        }
    }

    private NotFoundObjectResult TargetGone() => NotFound(new { message = "This target no longer exists." });

    private ConflictObjectResult DuplicateConflict() =>
        Conflict(new { message = "There's already a target for this KPI and year. Edit that one instead." });

    private static object TargetView(KpiTarget t) =>
        new
        {
            id = t.TargetId,
            t.MetricName,
            t.TargetValue,
            t.MeasurementUnit,
            t.TargetPeriod,
        };
}
