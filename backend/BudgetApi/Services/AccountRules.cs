using System.Text.RegularExpressions;

namespace BudgetApi.Services;

public static partial class AccountRules
{
    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    public static string CleanName(string? name) => Spaces().Replace(name?.Trim() ?? "", " ");

    public static bool IsValidEmail(string email) => email.Length <= 254 && EmailPattern().IsMatch(email);

    public static string? CleanIndustry(string? industry) => string.IsNullOrWhiteSpace(industry) ? null : industry.Trim();

    public static string? BusinessProblem(string name, string? industry, int? yearEndMonth) =>
        (name, industry, yearEndMonth) switch
        {
            ({ Length: 0 }, _, _) => "Type the business's registered name.",
            ({ Length: > 200 }, _, _) => "The registered name can be at most 200 characters.",
            (_, { Length: > 100 }, _) => "The industry can be at most 100 characters.",
            (_, _, null or < 1 or > 12) => "Choose the month the financial year ends (1 to 12).",
            _ => null,
        };
}
