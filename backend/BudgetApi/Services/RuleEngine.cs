// Rule engine: creates alerts when indicators pass thresholds
using System.Globalization;
using BudgetApi.Data;
using BudgetApi.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetApi.Services;

public class RuleEngine(BudgetDbContext db, ILogger<RuleEngine> logger)
{
    public async Task<IReadOnlyList<RuleAlert>> EvaluateIndicatorRulesAsync(
        string indicatorCode,
        IEnumerable<ExternalIndicator> indicators,
        CancellationToken cancellationToken = default
    )
    {
        var rules = await db
            .RuleDefinitions.Where(r =>
                r.IndicatorCode == indicatorCode
                && r.ActiveStatus == "active"
                && r.ThresholdValue != null
            )
            .ToListAsync(cancellationToken);

        if (rules.Count == 0)
        {
            logger.LogInformation("No active rules for {Code}; no alerts evaluated.", indicatorCode);
            return [];
        }

        var displayName = DisplayText.MidSentence(
            await db
                .IndicatorTypes.Where(t => t.IndicatorCode == indicatorCode)
                .Select(t => t.DisplayName)
                .FirstAsync(cancellationToken)
        );

        var categoryNames = await db
            .IndicatorCategoryLinks.Where(l => l.IndicatorCode == indicatorCode)
            .OrderBy(l => l.Category!.SortOrder)
            .Select(l => l.Category!.DisplayName)
            .ToListAsync(cancellationToken);

        var categories = categoryNames.Count > 0 ? string.Join(", ", categoryNames) : "no linked categories";

        var validated = indicators
            .Where(i => i.IndicatorCode == indicatorCode && i.ValidationStatus == "validated")
            .ToList();

        var alerts = new List<RuleAlert>();

        foreach (var rule in rules)
        {
            var threshold = rule.ThresholdValue!.Value;

            var seen = (
                await db
                    .RuleAlerts.Where(a => a.RuleId == rule.RuleId)
                    .Select(a => a.Message)
                    .ToListAsync(cancellationToken)
            ).ToHashSet();

            foreach (var indicator in validated.Where(i => i.IndicatorValue > threshold))
            {
                var message = string.Create(
                    CultureInfo.InvariantCulture,
                    $"South African {displayName} for {indicator.RelevantPeriod} "
                        + $"is {indicator.IndicatorValue:F2}% (threshold {threshold:F1}%). "
                        + $"Review forecasts for: {categories}."
                );

                if (!seen.Add(message))
                {
                    continue;
                }

                var alert = new RuleAlert
                {
                    RuleId = rule.RuleId,
                    Severity = "warning",
                    Message = message,
                    ResolutionStatus = "open",
                    CreatedAt = DateTime.UtcNow,
                };

                db.RuleAlerts.Add(alert);
                alerts.Add(alert);

                logger.LogInformation("Rule alert created: {Message}", message);
            }
        }

        if (alerts.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return alerts;
    }
}
