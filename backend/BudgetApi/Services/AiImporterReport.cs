using BudgetApi.Data;
using BudgetApi.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetApi.Services;

public static class AiImporterReport
{
    public static TimeZoneInfo SouthAfrica { get; } = Zone();

    public static DateTime MostRecentDueUtc(DateTime utcNow)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), SouthAfrica);
        var daysSinceSunday = (int)local.DayOfWeek;
        var sunday = DateOnly.FromDateTime(local).AddDays(-daysSinceSunday);
        var dueLocal = sunday.ToDateTime(new TimeOnly(23, 59));
        if (local < dueLocal)
        {
            dueLocal = dueLocal.AddDays(-7);
        }
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(dueLocal, DateTimeKind.Unspecified), SouthAfrica);
    }

    public static List<(string Label, string Category)> StillOpen(IEnumerable<(string Label, string Category, DateTime At)> rows)
    {
        return rows
            .GroupBy(row => StatementImporter.LabelKey(row.Label))
            .Select(group => group.OrderByDescending(row => row.At).First())
            .Where(row => !StatementImporter.ImporterCovers(row.Label))
            .OrderBy(row => row.Label, StringComparer.OrdinalIgnoreCase)
            .Select(row => (row.Label, row.Category))
            .ToList();
    }

    private static TimeZoneInfo Zone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("SAST", TimeSpan.FromHours(2), "SAST", "SAST");
        }
    }
}

public class AiImporterReportService(IServiceScopeFactory scopes, ILogger<AiImporterReportService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishDueReportsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "The weekly AI importer report did not run.");
            }
            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }

    private async Task PublishDueReportsAsync(CancellationToken cancellationToken)
    {
        var due = AiImporterReport.MostRecentDueUtc(DateTime.UtcNow);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        await RlsSession.SetBypassAsync(db, cancellationToken);
        var businesses = await db.AuditEntries.AsNoTracking()
            .Where(entry => entry.Area == "ai" && entry.Action == "categorised")
            .Select(entry => entry.BusinessId)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var businessId in businesses)
        {
            var already = await db.AuditEntries.AnyAsync(
                entry => entry.BusinessId == businessId && entry.Area == "ai" && entry.Action == "report" && entry.OccurredAt >= due,
                cancellationToken
            );
            if (already)
            {
                continue;
            }
            var rows = await db.AuditEntries.AsNoTracking()
                .Where(entry => entry.BusinessId == businessId && entry.Area == "ai" && entry.Action == "categorised")
                .Select(entry => new { entry.Summary, entry.OccurredAt })
                .ToListAsync(cancellationToken);
            var open = AiImporterReport.StillOpen(rows.Select(row => Split(row.Summary, row.OccurredAt)));
            var summary = open.Count == 0
                ? "Weekly report: the importer already covers every name the model has classified."
                : $"Weekly report: {open.Count} name{(open.Count == 1 ? "" : "s")} still need an importer rule.";
            db.AuditEntries.Add(new AuditEntry
            {
                BusinessId = businessId,
                UserName = "RandAhead",
                Area = "ai",
                Action = "report",
                Summary = summary,
                Details = open.Count == 0
                    ? null
                    : System.Text.Json.JsonSerializer.Serialize(open.Select(item => new AuditChange("Name", null, $"{item.Label} → {item.Category}"))),
                OccurredAt = due,
            });
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static (string Label, string Category, DateTime At) Split(string summary, DateTime at)
    {
        var parts = summary.Split(" → ", 2, StringSplitOptions.TrimEntries);
        return (parts[0], parts.Length == 2 ? parts[1] : "", at);
    }
}
