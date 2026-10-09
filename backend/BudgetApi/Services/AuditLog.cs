// Audit log: records who did what in the business, with before and after values, saved together with the change itself
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BudgetApi.Data;
using BudgetApi.Models;

namespace BudgetApi.Services;

public record AuditChange(
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("before")] string? Before,
    [property: JsonPropertyName("after")] string? After
);

public class AuditLog(BudgetDbContext db)
{
    public static readonly Dictionary<string, string> RoleNames = new()
    {
        ["submitter"] = "Submitter",
        ["approver"] = "Approver",
        ["decision_maker"] = "Decision Maker",
    };

    public static readonly Dictionary<string, string> MethodNames = new()
    {
        [ForecastRuns.MovingAverage] = "Moving Average",
        [ForecastRuns.ExponentialSmoothing] = "Exponential Smoothing",
        ["combined"] = "the average of both methods",
    };

    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public void Add(AppUser actor, string area, string action, string summary, IEnumerable<AuditChange>? details = null)
    {
        var changes = details?.ToList() ?? [];
        db.AuditEntries.Add(
            new AuditEntry
            {
                BusinessId = actor.Unit!.BusinessId,
                UserId = actor.UserId,
                UserName = actor.FullName,
                Area = area,
                Action = action,
                Summary = summary.Length > 500 ? summary[..500] : summary,
                Details = changes.Count == 0 ? null : JsonSerializer.Serialize(changes, Json),
            }
        );
    }

    public static IEnumerable<AuditChange> Changed(params AuditChange[] changes) => changes.Where(c => c.Before != c.After);

    public static string Rand(decimal value) =>
        "R " + Math.Round(value).ToString("#,0", CultureInfo.InvariantCulture).Replace(",", " ");

    public static string Number(decimal? value) =>
        value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "–";
}
