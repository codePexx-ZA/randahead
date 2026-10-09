// Activity log API: the admin reads everything that happened in the business, everyone else only their own actions
using System.Text.Json;
using BudgetApi.Data;
using BudgetApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BudgetApi.Controllers;

[ApiController]
[Route("api/audit")]
[Authorize]
public class AuditController(BudgetDbContext db, BusinessScope scope) : ControllerBase
{
    private const int PageSize = 200;
    private static readonly TimeSpan SouthAfrica = TimeSpan.FromHours(2);
    private static readonly HashSet<string> Areas = ["account", "upload", "forecast", "budget", "target", "rule", "business", "user"];

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? area,
        [FromQuery] string? user,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken
    )
    {
        if (await scope.CallerAsync(User, cancellationToken) is not { } caller)
        {
            return Unauthorized(new { message = "Your session has ended. Sign in again." });
        }
        if (area is not null && !Areas.Contains(area))
        {
            return BadRequest(new { message = "Choose one of the listed areas." });
        }
        if (from > to)
        {
            return BadRequest(new { message = "The start date must be on or before the end date." });
        }

        var businessId = caller.Unit!.BusinessId;
        var query = db.AuditEntries.AsNoTracking().Where(e => e.BusinessId == businessId);
        query = caller.IsAdmin
            ? user is null ? query : query.Where(e => e.UserId == user)
            : query.Where(e => e.UserId == caller.UserId);
        if (area is not null)
        {
            query = query.Where(e => e.Area == area);
        }
        if (from is { } start)
        {
            var since = StartOfDay(start);
            query = query.Where(e => e.OccurredAt >= since);
        }
        if (to is { } end)
        {
            var until = StartOfDay(end.AddDays(1));
            query = query.Where(e => e.OccurredAt < until);
        }

        var entries = await query
            .Include(e => e.User)
            .OrderByDescending(e => e.OccurredAt)
            .Take(PageSize + 1)
            .ToListAsync(cancellationToken);

        var people = caller.IsAdmin
            ? (
                await db
                    .AppUsers.AsNoTracking()
                    .Where(u => u.Unit!.BusinessId == businessId)
                    .OrderBy(u => u.FullName)
                    .ToListAsync(cancellationToken)
            ).Select(u => new { id = u.UserId, name = DisplayText.Person(u) })
            : null;

        return Ok(
            new
            {
                scope = caller.IsAdmin ? "business" : "own",
                more = entries.Count > PageSize,
                people,
                entries = entries.Take(PageSize).Select(e => new
                {
                    id = e.LogId,
                    at = DateTime.SpecifyKind(e.OccurredAt, DateTimeKind.Utc),
                    who = e.User is null ? $"{e.UserName} (former user)" : DisplayText.Person(e.User),
                    e.Area,
                    e.Action,
                    e.Summary,
                    details = e.Details is null ? [] : JsonSerializer.Deserialize<AuditChange[]>(e.Details) ?? [],
                }),
            }
        );
    }

    private static DateTime StartOfDay(DateOnly day) =>
        new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), SouthAfrica).UtcDateTime;
}
