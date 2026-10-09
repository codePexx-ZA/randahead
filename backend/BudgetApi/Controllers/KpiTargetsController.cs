// KPI targets API: lists, adds, changes and deletes the caller's unit's goals per KPI per financial year
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
public class KpiTargetsController(BudgetDbContext db, BusinessScope scope, AuditLog audit) : ControllerBase
{
    private const string TargetEditors = "decision_maker";
    private static readonly HashSet<string> RandSubtotals = ["net_profit", "operating_profit", "gross_profit", "profit_before_tax"];
    private static readonly HashSet<string> PercentMargins = ["net_margin", "operating_margin", "gross_margin"];
    private static readonly Dictionary<string, string> KpiNames = new()
    {
        ["net_profit"] = "Net profit",
        ["operating_profit"] = "Operating profit",
        ["gross_profit"] = "Gross profit",
        ["profit_before_tax"] = "Profit before tax",
        ["net_margin"] = "Net margin",
        ["operating_margin"] = "Operating margin",
        ["gross_margin"] = "Gross margin",
    };

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (await scope.CallerAsync(User, cancellationToken) is not { } caller)
        {
            return SessionEnded();
        }
        var targets = await db
            .KpiTargets.AsNoTracking()
            .Where(t => t.UnitId == caller.UnitId)
            .OrderBy(t => t.TargetPeriod)
            .ThenBy(t => t.MetricName)
            .ToListAsync(cancellationToken);
        return Ok(targets.Select(TargetView));
    }

    [HttpPost]
    [Authorize(Roles = TargetEditors)]
    public async Task<IActionResult> Create(KpiTargetRequest request, CancellationToken cancellationToken)
    {
        if (await scope.CallerAsync(User, cancellationToken) is not { } caller)
        {
            return SessionEnded();
        }
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
    [Authorize(Roles = TargetEditors)]
    public async Task<IActionResult> Update(string id, KpiTargetRequest request, CancellationToken cancellationToken)
    {
        if (await scope.CallerAsync(User, cancellationToken) is not { } caller)
        {
            return SessionEnded();
        }
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
    [Authorize(Roles = TargetEditors)]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        if (await scope.CallerAsync(User, cancellationToken) is not { } caller)
        {
            return SessionEnded();
        }
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

    private async Task<string> KpiNameAsync(string metric, CancellationToken cancellationToken) =>
        KpiNames.TryGetValue(metric, out var name)
            ? name
            : await db.LineCategories.Where(c => c.CategoryCode == metric).Select(c => c.DisplayName).FirstOrDefaultAsync(cancellationToken)
                ?? metric;

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
        var metric = request.MetricName ?? "";
        var unit = PercentMargins.Contains(metric) ? "percent"
            : RandSubtotals.Contains(metric) || await db.LineCategories.AnyAsync(c => c.CategoryCode == metric, cancellationToken) ? "rand"
            : null;
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

    private UnauthorizedObjectResult SessionEnded() => Unauthorized(new { message = "Your session has ended. Sign in again." });

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
