// Warning rules API: lists, adds, changes, switches off and deletes the caller's business's indicator warning rules
using System.Text.Json.Serialization;
using BudgetApi.Data;
using BudgetApi.Models;
using BudgetApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BudgetApi.Controllers;

public record RuleRequest(
    [property: JsonPropertyName("rule_name")] string? RuleName,
    [property: JsonPropertyName("indicator_code")] string? IndicatorCode,
    [property: JsonPropertyName("threshold_value")] decimal? ThresholdValue,
    [property: JsonPropertyName("active_status")] string? ActiveStatus
);

[ApiController]
[Route("api/rules")]
[Authorize]
public class RulesController(BudgetDbContext db, BusinessScope scope, AuditLog audit) : ControllerBase
{
    private const string RuleEditors = "approver,decision_maker";

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (await scope.CallerAsync(User, cancellationToken) is not { } caller)
        {
            return SessionEnded();
        }
        var rules = await BusinessRules(caller.Unit!.BusinessId)
            .AsNoTracking()
            .OrderBy(r => r.RuleName)
            .Select(r => new { Rule = r, HasAlerts = r.Alerts.Any() })
            .ToListAsync(cancellationToken);
        return Ok(rules.Select(r => RuleView(r.Rule, !r.HasAlerts)));
    }

    [HttpPost]
    [Authorize(Roles = RuleEditors)]
    public async Task<IActionResult> Create(RuleRequest request, CancellationToken cancellationToken)
    {
        if (await scope.CallerAsync(User, cancellationToken) is not { } caller)
        {
            return SessionEnded();
        }
        if (await ProblemAsync(request, cancellationToken) is { } problem)
        {
            return BadRequest(new { message = problem });
        }

        var businessId = caller.Unit!.BusinessId;
        var name = request.RuleName!.Trim();
        if (await NameTakenAsync(businessId, name, null, cancellationToken))
        {
            return NameConflict(name);
        }

        var rule = new RuleDefinition { BusinessId = businessId };
        Apply(rule, request);
        db.RuleDefinitions.Add(rule);
        var after = await DescribeAsync(rule, cancellationToken);
        audit.Add(
            caller,
            "rule",
            "added",
            $"Added warning rule \"{rule.RuleName}\"",
            AuditLog.Changed(
                new AuditChange("Indicator", null, after.Indicator),
                new AuditChange("Threshold", null, after.Threshold),
                new AuditChange("Status", null, after.Status)
            )
        );
        return await SaveAsync(name, cancellationToken) ?? Ok(RuleView(rule, deletable: true));
    }

    [HttpPut("{id}")]
    [Authorize(Roles = RuleEditors)]
    public async Task<IActionResult> Update(string id, RuleRequest request, CancellationToken cancellationToken)
    {
        if (await scope.CallerAsync(User, cancellationToken) is not { } caller)
        {
            return SessionEnded();
        }
        if (await ProblemAsync(request, cancellationToken) is { } problem)
        {
            return BadRequest(new { message = problem });
        }

        var businessId = caller.Unit!.BusinessId;
        if (await BusinessRules(businessId).FirstOrDefaultAsync(r => r.RuleId == id, cancellationToken) is not { } rule)
        {
            return RuleGone();
        }
        var name = request.RuleName!.Trim();
        if (await NameTakenAsync(businessId, name, id, cancellationToken))
        {
            return NameConflict(name);
        }

        var before = await DescribeAsync(rule, cancellationToken);
        var oldName = rule.RuleName;
        Apply(rule, request);
        var after = await DescribeAsync(rule, cancellationToken);
        var changes = AuditLog
            .Changed(
                new AuditChange("Name", oldName, rule.RuleName),
                new AuditChange("Indicator", before.Indicator, after.Indicator),
                new AuditChange("Threshold", before.Threshold, after.Threshold),
                new AuditChange("Status", before.Status, after.Status)
            )
            .ToList();
        if (changes.Count > 0)
        {
            audit.Add(caller, "rule", "changed", $"Changed warning rule \"{rule.RuleName}\"", changes);
        }
        var hasAlerts = await db.RuleAlerts.AnyAsync(a => a.RuleId == id, cancellationToken);
        return await SaveAsync(name, cancellationToken) ?? Ok(RuleView(rule, !hasAlerts));
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = RuleEditors)]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        if (await scope.CallerAsync(User, cancellationToken) is not { } caller)
        {
            return SessionEnded();
        }
        if (await BusinessRules(caller.Unit!.BusinessId).FirstOrDefaultAsync(r => r.RuleId == id, cancellationToken) is not { } rule)
        {
            return RuleGone();
        }
        if (await db.RuleAlerts.AnyAsync(a => a.RuleId == id, cancellationToken))
        {
            return OnRecordConflict();
        }

        db.RuleDefinitions.Remove(rule);
        var before = await DescribeAsync(rule, cancellationToken);
        audit.Add(
            caller,
            "rule",
            "deleted",
            $"Deleted warning rule \"{rule.RuleName}\"",
            [new AuditChange("Indicator", before.Indicator, null), new AuditChange("Threshold", before.Threshold, null)]
        );
        return await SaveAsync(rule.RuleName, cancellationToken) ?? NoContent();
    }

    private async Task<(string Indicator, string Threshold, string Status)> DescribeAsync(
        RuleDefinition rule,
        CancellationToken cancellationToken
    )
    {
        var indicator = await db
            .IndicatorTypes.Where(t => t.IndicatorCode == rule.IndicatorCode)
            .Select(t => t.DisplayName)
            .FirstOrDefaultAsync(cancellationToken);
        return (
            indicator ?? rule.IndicatorCode ?? "None",
            $"{AuditLog.Number(rule.ThresholdValue)}%",
            rule.ActiveStatus == "active" ? "On" : "Off"
        );
    }

    private IQueryable<RuleDefinition> BusinessRules(string businessId) =>
        db.RuleDefinitions.Where(r => r.BusinessId == businessId);

    private Task<bool> NameTakenAsync(string businessId, string name, string? ownId, CancellationToken cancellationToken) =>
        BusinessRules(businessId).AnyAsync(r => r.RuleName.ToLower() == name.ToLower() && r.RuleId != ownId, cancellationToken);

    private async Task<string?> ProblemAsync(RuleRequest request, CancellationToken cancellationToken)
    {
        var name = request.RuleName?.Trim() ?? "";
        if (name.Length == 0)
        {
            return "Type a name for the rule.";
        }
        if (name.Length > 200)
        {
            return "Keep the rule name under 200 characters.";
        }
        if (request.ThresholdValue is not { } threshold || threshold < 0 || threshold > 100)
        {
            return "Type a threshold between 0 and 100, e.g. 6 or 7.5.";
        }
        if (request.ActiveStatus is not ("active" or "inactive"))
        {
            return "The status must be active or inactive.";
        }
        var code = request.IndicatorCode ?? "";
        if (!await db.IndicatorTypes.AnyAsync(t => t.IndicatorCode == code, cancellationToken))
        {
            return "Choose one of the listed economic indicators.";
        }
        return null;
    }

    private static void Apply(RuleDefinition rule, RuleRequest request)
    {
        var code = request.IndicatorCode!;
        rule.RuleName = request.RuleName!.Trim();
        rule.IndicatorCode = code;
        rule.ThresholdValue = request.ThresholdValue;
        rule.ActiveStatus = request.ActiveStatus!;
        rule.Condition = DefaultRules.Condition(code);
        rule.Action = DefaultRules.Action;
    }

    private async Task<IActionResult?> SaveAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return NameConflict(name);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            return OnRecordConflict();
        }
    }

    private UnauthorizedObjectResult SessionEnded() => Unauthorized(new { message = "Your session has ended. Sign in again." });

    private NotFoundObjectResult RuleGone() => NotFound(new { message = "This rule no longer exists." });

    private ConflictObjectResult NameConflict(string name) =>
        Conflict(new { message = $"A rule called \"{name}\" already exists. Pick another name." });

    private ConflictObjectResult OnRecordConflict() =>
        Conflict(new { message = "This rule has raised warnings before, so it can only be switched off." });

    private static object RuleView(RuleDefinition rule, bool deletable) =>
        new
        {
            id = rule.RuleId,
            name = rule.RuleName,
            rule.IndicatorCode,
            threshold = rule.ThresholdValue,
            active = rule.ActiveStatus == "active",
            deletable,
        };
}
