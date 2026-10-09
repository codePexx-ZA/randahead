// Default warning rules: the four indicator rules every new business starts with, and the rule wording
using BudgetApi.Models;

namespace BudgetApi.Services;

public static class DefaultRules
{
    public const string Action = "Create warning alert: review linked expense forecasts";

    private static readonly (string Name, string IndicatorCode, decimal Threshold)[] Rules =
    [
        ("Headline CPI above threshold", "cpi_headline", 6.0m),
        ("Core inflation above threshold", "cpi_core", 6.0m),
        ("Fuel price increase above threshold", "fuel_price", 10.0m),
        ("Electricity price increase above threshold", "electricity_price", 10.0m),
    ];

    public static string Condition(string indicatorCode) => $"indicator == {indicatorCode} AND value > threshold_value";

    public static IEnumerable<RuleDefinition> For(string businessId) =>
        Rules.Select(r => new RuleDefinition
        {
            BusinessId = businessId,
            RuleName = r.Name,
            IndicatorCode = r.IndicatorCode,
            ThresholdValue = r.Threshold,
            Condition = Condition(r.IndicatorCode),
            Action = Action,
        });
}
