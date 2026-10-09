// Display text: how people, dates and names inside sentences are shown in API responses and messages
using System.Globalization;
using BudgetApi.Models;

namespace BudgetApi.Services;

public static class DisplayText
{
    public static string Person(AppUser? user) =>
        user is null ? "Unknown user" : user.DeletedAt is null ? user.FullName : $"{user.FullName} (former user)";

    public static string MidSentence(string name) =>
        name.Length > 1 && char.IsUpper(name[0]) && !char.IsUpper(name[1])
            ? char.ToLowerInvariant(name[0]) + name[1..]
            : name;

    public static string? Day(DateTime? moment) => moment?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
