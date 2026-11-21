namespace BudgetApi.Services;

public static class DefaultRules
{
    public const string Action = "Create warning alert: review linked expense forecasts";

    public static string Condition(string indicatorCode) => $"indicator == {indicatorCode} AND value > threshold_value";
}
