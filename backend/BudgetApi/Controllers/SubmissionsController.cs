// Submissions API: income statement uploads, the approver's decisions on them and the business's remembered line mappings
using System.Text.Json;
using System.Text.RegularExpressions;
using BudgetApi.Data;
using BudgetApi.Models;
using BudgetApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BudgetApi.Controllers;

public record SubmissionRowRequest(
    string? Label,
    string? Kind,
    string? Subtotal,
    decimal?[]? Values,
    MappingPart[]? Mapping,
    bool Remember
);

public record SubmissionRequest(
    string? FileName,
    string? Frequency,
    int Scale,
    string[]? Years,
    string? ReplacesSubmissionId,
    SubmissionRowRequest[]? Rows
);

public record DecisionRequest(string? Decision, string? Comments);

public record SubmissionRowView(
    string Label,
    string Kind,
    string? Subtotal,
    decimal?[] Values,
    MappingPart[]? Mapping,
    bool Remember
);

public record SubmissionView(
    string Id,
    string FileName,
    string[] Years,
    string[] Periods,
    int Scale,
    SubmissionRowView[]? Rows,
    string SubmittedBy,
    string? SubmittedOn,
    string Status,
    string? ReplacesId,
    string? Decision,
    string? DecidedBy,
    string? DecidedOn,
    string? Comment,
    string? SupersededBy
);

[ApiController]
[Route("api/submissions")]
[Authorize]
public partial class SubmissionsController(BudgetDbContext db, BusinessScope scope, AuditLog audit) : ControllerBase
{
    private const int MaxYears = StatementImporter.MaxYears;
    private const int MaxRows = StatementImporter.MaxRows;
    private const long MaxFileBytes = 5 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> RowKinds = ["heading", "line", "subtotal", "total"];
    private static readonly HashSet<string> Subtotals = ["gross_profit", "operating_profit", "profit_before_tax", "net_profit"];
    private static readonly HashSet<int> Scales = [1, 1000, 1000000];

    [GeneratedRegex(@"^FY\d{4}$")]
    private static partial Regex YearLabel();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken cancellationToken)
    {
        if (await scope.CallerAsync(User, cancellationToken) is not { } caller)
        {
            return SessionEnded();
        }
        var submissions = await LoadAsync(caller.UnitId, tracked: false, cancellationToken);
        var views = submissions.Select(s => View(s, submissions, caller.Unit!.Business!.YearEndMonth));
        return Ok(status is null ? views : views.Where(v => v.Status == status));
    }

    [HttpGet("template")]
    public async Task<IActionResult> Template(CancellationToken cancellationToken)
    {
        if (await scope.CallerAsync(User, cancellationToken) is not { } caller)
        {
            return SessionEnded();
        }
        var names = await db.LineCategories.AsNoTracking().ToDictionaryAsync(c => c.CategoryCode, c => c.DisplayName, cancellationToken);
        var business = caller.Unit!.Business!;
        var file = StatementTemplate.Build(business.LegalName, business.YearEndMonth, names, DateOnly.FromDateTime(DateTime.Now));
        return File(file, StatementTemplate.ContentType, "RandAhead_income_statement_template.xlsx");
    }

    [HttpPost("preview")]
    [RequestSizeLimit(MaxFileBytes + 64 * 1024)]
    public async Task<IActionResult> Preview(IFormFile? file, CancellationToken cancellationToken)
    {
        if (await scope.CallerAsync(User, cancellationToken) is not { } caller)
        {
            return SessionEnded();
        }
        if (caller.Role != "submitter" && !caller.IsAdmin)
        {
            return Forbidden("Only a Submitter can upload income statements.");
        }
        var extension = Path.GetExtension(file?.FileName ?? "").ToLowerInvariant();
        var problem = true switch
        {
            _ when file is null || file.Length == 0 => "Choose a file to upload.",
            _ when extension == ".xls" => "Old .xls files can't be read. Open the file in Excel and save it as .xlsx (or .csv).",
            _ when extension is not (".xlsx" or ".csv") => "Please choose an Excel (.xlsx) or CSV file.",
            _ when file.Length > MaxFileBytes => "That file is over 5 MB. Remove extra sheets or images and try again.",
            _ => null,
        };
        if (problem is not null)
        {
            return BadRequest(new { message = problem });
        }

        var remembered = await RememberedAsync(caller.Unit!.BusinessId, cancellationToken);
        await using var stream = new MemoryStream();
        await file!.CopyToAsync(stream, cancellationToken);
        stream.Position = 0;
        try
        {
            return Ok(StatementImporter.Read(stream, Path.GetFileName(file.FileName), remembered));
        }
        catch (StatementImportException error)
        {
            return BadRequest(new { message = error.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create(SubmissionRequest request, CancellationToken cancellationToken)
    {
        if (await scope.CallerAsync(User, cancellationToken) is not { } caller)
        {
            return SessionEnded();
        }
        if (caller.Role != "submitter" && !caller.IsAdmin)
        {
            return Forbidden("Only a Submitter can upload income statements.");
        }
        var categories = await db.LineCategories.Select(c => c.CategoryCode).ToListAsync(cancellationToken);
        if (Problem(request, categories.ToHashSet()) is { } problem)
        {
            return BadRequest(new { message = problem });
        }

        var submissions = await LoadAsync(caller.UnitId, tracked: false, cancellationToken);
        if (request.ReplacesSubmissionId is { } replacesId)
        {
            if (submissions.FirstOrDefault(s => s.SubmissionId == replacesId) is not { Status: "rejected" })
            {
                return Conflict(new { message = "Only a rejected upload can be fixed and re-uploaded." });
            }
            if (submissions.Any(s => s.ReplacesSubmissionId == replacesId))
            {
                return Conflict(new { message = "This rejected upload has already been fixed and sent again." });
            }
        }

        var yearEndMonth = caller.Unit!.Business!.YearEndMonth;
        var order = Enumerable.Range(0, request.Years!.Length).OrderBy(i => request.Years[i], StringComparer.Ordinal).ToArray();
        var starts = order.Select(i => FinancialYear.Start(request.Years![i], yearEndMonth)).ToArray();
        var rows = request.Rows!;
        var submission = new BudgetSubmission
        {
            UnitId = caller.UnitId,
            SubmittedBy = caller.UserId,
            Submitter = caller,
            PeriodStart = starts[0],
            PeriodEnd = starts[^1].AddYears(1).AddDays(-1),
            Status = "submitted",
            SubmittedAt = DateTime.UtcNow,
            ReplacesSubmissionId = request.ReplacesSubmissionId,
            ImportMethod = rows.Any(r => r.Kind == "line" && r.Remember) ? "mapped_import" : "template",
            SourceFileName = request.FileName!.Trim(),
            SourceScale = request.Scale,
        };

        for (var i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            var isLine = row.Kind == "line";
            submission.Rows.Add(
                new SubmissionRow
                {
                    RowOrder = (short)(i + 1),
                    RowKind = row.Kind!,
                    SourceLabel = CleanLabel(row.Label),
                    SubtotalCode = row.Kind == "subtotal" ? row.Subtotal : null,
                    Amounts = order.Select(j => row.Values![j]).ToArray(),
                    Mapping = isLine ? JsonSerializer.Serialize(row.Mapping ?? [], Json) : null,
                    HandMapped = isLine && row.Remember,
                }
            );
        }

        var totals = categories.ToDictionary(c => c, _ => new decimal[starts.Length]);
        foreach (var row in rows.Where(r => r.Kind == "line"))
        {
            foreach (var part in row.Mapping ?? [])
            {
                for (var k = 0; k < starts.Length; k++)
                {
                    totals[part.Category][k] += Math.Abs((row.Values![order[k]] ?? 0) * part.Percent / 100m) * request.Scale;
                }
            }
        }
        foreach (var (category, amounts) in totals)
        {
            for (var k = 0; k < starts.Length; k++)
            {
                submission.LineItems.Add(
                    new FinancialLineItem { Category = category, PeriodStart = starts[k], Amount = Math.Round(amounts[k], 2) }
                );
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await RememberMappingsAsync(caller, rows, cancellationToken);
        db.BudgetSubmissions.Add(submission);
        var sorted = request.Years!.Order(StringComparer.Ordinal).ToArray();
        var span = sorted.Length == 1 ? sorted[0] : $"{sorted[0]}–{sorted[^1]}";
        var fixing = request.ReplacesSubmissionId is null ? "" : " to fix a rejected upload";
        audit.Add(caller, "upload", "uploaded", $"Uploaded {submission.SourceFileName} ({span}){fixing}");
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(View(submission, [submission], yearEndMonth));
    }

    [HttpPost("{id}/decision")]
    public async Task<IActionResult> Decide(string id, DecisionRequest request, CancellationToken cancellationToken)
    {
        if (await scope.CallerAsync(User, cancellationToken) is not { } caller)
        {
            return SessionEnded();
        }
        if (caller.Role != "approver" && !caller.IsAdmin)
        {
            return Forbidden("Only an Approver can approve or reject uploads.");
        }

        var comments = string.IsNullOrWhiteSpace(request.Comments) ? null : request.Comments.Trim();
        var problem = (request.Decision, comments) switch
        {
            (not ("approved" or "rejected"), _) => "The decision must be approved or rejected.",
            ("rejected", null) => "Add a comment so the submitter knows what to fix.",
            (_, { Length: > 2000 }) => "The comment can be at most 2000 characters.",
            _ => null,
        };
        if (problem is not null)
        {
            return BadRequest(new { message = problem });
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var submissions = await LoadAsync(caller.UnitId, tracked: true, cancellationToken);
        if (submissions.FirstOrDefault(s => s.SubmissionId == id) is not { } target)
        {
            return NotFound(new { message = "This upload no longer exists." });
        }
        if (target.Status != "submitted")
        {
            return Conflict(new { message = "This upload has already been decided." });
        }

        target.Status = request.Decision!;
        target.Approvals.Add(
            new ApprovalAction
            {
                SubmissionId = target.SubmissionId,
                ActedBy = caller.UserId,
                Actor = caller,
                Decision = request.Decision!,
                Comments = comments,
            }
        );
        if (target.Status == "approved")
        {
            if (submissions.FirstOrDefault(s => s.SubmissionId == target.ReplacesSubmissionId) is { Status: "rejected" } fixedUpload)
            {
                fixedUpload.Status = "superseded";
            }
            SupersedeCovered(submissions);
        }

        audit.Add(
            caller,
            "upload",
            target.Status,
            $"{(target.Status == "approved" ? "Approved" : "Rejected")} upload {target.SourceFileName}",
            comments is null ? null : [new AuditChange("Comment", null, comments)]
        );
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(View(target, submissions, caller.Unit!.Business!.YearEndMonth));
    }

    private async Task<List<BudgetSubmission>> LoadAsync(string unitId, bool tracked, CancellationToken cancellationToken)
    {
        var query = db
            .BudgetSubmissions.Where(s => s.UnitId == unitId && s.Status != "draft")
            .Include(s => s.Submitter)
            .Include(s => s.Rows)
            .Include(s => s.LineItems)
            .Include(s => s.Approvals)
            .ThenInclude(a => a.Actor)
            .OrderByDescending(s => s.SubmittedAt)
            .AsSplitQuery();
        return await (tracked ? query : query.AsNoTracking()).ToListAsync(cancellationToken);
    }

    private async Task<Dictionary<string, MappingPart[]>> RememberedAsync(string businessId, CancellationToken cancellationToken)
    {
        var mappings = await db
            .ImportMappings.AsNoTracking()
            .Where(m => m.BusinessId == businessId)
            .OrderBy(m => m.LabelKey)
            .ThenByDescending(m => m.SplitPercent)
            .ToListAsync(cancellationToken);
        return mappings
            .GroupBy(m => m.LabelKey)
            .ToDictionary(g => g.Key, g => g.Select(m => new MappingPart(m.CategoryCode, m.SplitPercent)).ToArray());
    }

    private async Task RememberMappingsAsync(AppUser caller, SubmissionRowRequest[] rows, CancellationToken cancellationToken)
    {
        var businessId = caller.Unit!.BusinessId;
        var remembered = rows
            .Where(r => r.Kind == "line" && r.Remember && r.Mapping is { Length: > 0 })
            .GroupBy(r => LabelKey(r.Label!))
            .Select(g => g.Last())
            .ToList();
        if (remembered.Count == 0)
        {
            return;
        }

        var keys = remembered.Select(r => LabelKey(r.Label!)).ToList();
        await db
            .ImportMappings.Where(m => m.BusinessId == businessId && keys.Contains(m.LabelKey))
            .ExecuteDeleteAsync(cancellationToken);
        db.ImportMappings.AddRange(
            remembered.SelectMany(r =>
                r.Mapping!.Select(p => new ImportMapping
                {
                    BusinessId = businessId,
                    SourceLabel = CleanLabel(r.Label),
                    LabelKey = LabelKey(r.Label!),
                    CategoryCode = p.Category,
                    SplitPercent = p.Percent,
                    CreatedBy = caller.UserId,
                })
            )
        );
    }

    private static void SupersedeCovered(List<BudgetSubmission> newestFirst)
    {
        var approved = newestFirst.Where(s => s.Status == "approved").ToList();
        var covered = new HashSet<DateOnly>();
        foreach (var submission in approved)
        {
            var periods = Periods(submission);
            if (covered.Count > 0 && periods.All(covered.Contains))
            {
                submission.Status = "superseded";
            }
            covered.UnionWith(periods);
        }
    }

    private static SubmissionView View(BudgetSubmission s, List<BudgetSubmission> all, int yearEndMonth)
    {
        var periods = Periods(s);
        var labels = periods.Select(p => FinancialYear.Label(p, yearEndMonth)).ToArray();
        var decision = s.Approvals.OrderByDescending(a => a.ActedAt).FirstOrDefault();
        var rows = s.Rows.Count == 0
            ? null
            : s.Rows.OrderBy(r => r.RowOrder).Select(r => new SubmissionRowView(
                r.SourceLabel,
                r.RowKind,
                r.SubtotalCode,
                r.Amounts,
                r.Mapping is null ? null : JsonSerializer.Deserialize<MappingPart[]>(r.Mapping, Json),
                r.HandMapped
            )).ToArray();

        return new SubmissionView(
            s.SubmissionId,
            s.SourceFileName ?? "Untitled upload",
            labels.Length == 0 ? [] : [labels[0], labels[^1]],
            labels,
            s.SourceScale,
            rows,
            DisplayText.Person(s.Submitter),
            DisplayText.Day(s.SubmittedAt),
            s.Status,
            s.ReplacesSubmissionId,
            decision?.Decision,
            decision is null ? null : DisplayText.Person(decision.Actor),
            DisplayText.Day(decision?.ActedAt),
            decision?.Comments,
            s.Status == "superseded" ? ReplacedBy(s, periods, all) : null
        );
    }

    private static string? ReplacedBy(BudgetSubmission s, List<DateOnly> periods, List<BudgetSubmission> newestFirst) =>
        newestFirst.FirstOrDefault(x => x.ReplacesSubmissionId == s.SubmissionId)?.SubmissionId
        ?? newestFirst
            .LastOrDefault(x =>
                x.SubmittedAt > s.SubmittedAt
                && x.Status is "approved" or "superseded"
                && x.Approvals.Any(a => a.Decision == "approved")
                && Periods(x).Intersect(periods).Any()
            )
            ?.SubmissionId;

    private static List<DateOnly> Periods(BudgetSubmission s) =>
        s.LineItems.Select(i => i.PeriodStart).Distinct().Order().ToList();

    private static string? Problem(SubmissionRequest request, HashSet<string> categories)
    {
        var years = request.Years ?? [];
        var rows = request.Rows ?? [];
        var fileName = request.FileName?.Trim() ?? "";
        var general = true switch
        {
            _ when fileName.Length is 0 or > 255 => "The file name is missing or longer than 255 characters.",
            _ when request.Frequency != "annual" => "Only yearly income statements can be uploaded for now.",
            _ when !Scales.Contains(request.Scale) => "The amounts must be in rand, R'000 or R million.",
            _ when years.Length is 0 or > MaxYears => $"Upload 1 to {MaxYears} financial years.",
            _ when years.Any(y => !YearLabel().IsMatch(y)) => "Year headings must look like FY2025.",
            _ when years.Distinct().Count() != years.Length => "Each financial year can appear only once.",
            _ when rows.Length is 0 or > MaxRows => $"The statement needs 1 to {MaxRows} rows.",
            _ => null,
        };
        if (general is not null)
        {
            return general;
        }

        foreach (var row in rows)
        {
            var label = CleanLabel(row.Label);
            var rowProblem = true switch
            {
                _ when row.Kind is null || !RowKinds.Contains(row.Kind) => "Every row must be a heading, line, subtotal or total.",
                _ when label.Length is 0 or > 200 => "Every row needs a label of at most 200 characters.",
                _ when row.Values?.Length != years.Length => $"\"{label}\" needs one amount per financial year.",
                _ when row.Kind == "subtotal" && (row.Subtotal is null || !Subtotals.Contains(row.Subtotal)) => $"\"{label}\" is not a known subtotal.",
                _ when row.Kind == "line" && row.Mapping is null => $"\"{label}\" needs a category.",
                _ => null,
            };
            if (rowProblem is not null)
            {
                return rowProblem;
            }
            if (row.Kind == "line" && MappingProblem(label, row.Mapping!, categories) is { } mappingProblem)
            {
                return mappingProblem;
            }
        }

        return rows.Any(r => r.Kind == "line" && r.Mapping!.Any(p => p.Category == "revenue"))
            ? null
            : "No line is matched to Revenue. Every income statement needs sales or revenue.";
    }

    private static string? MappingProblem(string label, MappingPart[] parts, HashSet<string> categories) =>
        true switch
        {
            _ when parts.Any(p => !categories.Contains(p.Category)) => $"\"{label}\" is matched to an unknown category.",
            _ when parts.Any(p => p.Percent is <= 0 or > 100) => $"Each share of \"{label}\" must be above 0% and at most 100%.",
            _ when parts.Select(p => p.Category).Distinct().Count() != parts.Length => $"\"{label}\" lists a category twice.",
            _ when parts.Length > 0 && parts.Sum(p => p.Percent) != 100 => $"The shares of \"{label}\" must add up to 100%.",
            _ => null,
        };

    private static string CleanLabel(string? label) => Spaces().Replace(label?.Trim() ?? "", " ");

    private static string LabelKey(string label) => StatementImporter.LabelKey(label);

    private ObjectResult Forbidden(string message) => StatusCode(StatusCodes.Status403Forbidden, new { message });

    private UnauthorizedObjectResult SessionEnded() => Unauthorized(new { message = "Your session has ended. Sign in again." });
}
