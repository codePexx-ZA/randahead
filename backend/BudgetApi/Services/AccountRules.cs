// Account rules: shared checks for business details, people's names, emails and passwords
using System.Text.RegularExpressions;

namespace BudgetApi.Services;

public static partial class AccountRules
{
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 72;

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    public static string CleanName(string? name) => Spaces().Replace(name?.Trim() ?? "", " ");

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

    public static string? PersonProblem(string name, string? email, string? password, bool passwordRequired)
    {
        var cleanEmail = email?.Trim() ?? "";
        var cleanPassword = password ?? "";
        return true switch
        {
            _ when name.Length == 0 => "Type the person's full name.",
            _ when name.Length > 200 => "The full name can be at most 200 characters.",
            _ when cleanEmail.Length > 254 || !EmailPattern().IsMatch(cleanEmail) =>
                "Type a valid email address, e.g. name@business.co.za.",
            _ when passwordRequired && cleanPassword.Length == 0 => "Type a password.",
            _ when cleanPassword.Length is > 0 and < MinPasswordLength => $"The password needs at least {MinPasswordLength} characters.",
            _ when cleanPassword.Length > MaxPasswordLength => $"The password can be at most {MaxPasswordLength} characters.",
            _ => null,
        };
    }

    public static string? NewPasswordProblem(string? password) =>
        (password ?? "").Length switch
        {
            0 => "Type a new password.",
            < MinPasswordLength => $"The password needs at least {MinPasswordLength} characters.",
            > MaxPasswordLength => $"The password can be at most {MaxPasswordLength} characters.",
            _ => null,
        };

    public static string HashPassword(string password) => BCrypt.Net.BCrypt.HashPassword(password, 11);
}
