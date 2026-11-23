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
    SubmissionRowRequest[]? Rows
);

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
    string? SupersededBy,
    StatementTable? Statement
);

[ApiController]
[Route("api/submissions")]
[Authorize]
public partial class SubmissionsController(BudgetDbContext db, BusinessScope scope, AuditLog audit, LineCategoriser lines) : ApiController
{
    private const int MaxYears = StatementImporter.MaxYears;
    private const int MaxRows = StatementImporter.MaxRows;
    private const long MaxFileBytes = 32 * 1024 * 1024;
    private const decimal MaxRandAmount = 10_000_000_000_000m;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> RowKinds = ["heading", "line", "subtotal", "total"];
    private static readonly HashSet<int> Scales = [1, 1000, 1000000];

    [GeneratedRegex(@"^FY\d{4}$")]
    private static partial Regex YearLabel();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken cancellationToken)
    {
        var caller = Caller;
        var submissions = await LoadAsync(caller.UnitId, tracked: false, cancellationToken);
        var views = submissions.Select(s => View(s, submissions, caller.Unit!.Business!.YearEndMonth));
        return Ok(status is null ? views : views.Where(v => v.Status == status));
    }

    [HttpGet("template")]
    public async Task<IActionResult> Template(CancellationToken cancellationToken)
    {
        var caller = Caller;
        var names = await db.LineCategories.AsNoTracking().ToDictionaryAsync(c => c.CategoryCode, c => c.DisplayName, cancellationToken);
        var business = caller.Unit!.Business!;
        var file = StatementTemplate.Build(business.LegalName, business.YearEndMonth, names, DateOnly.FromDateTime(DateTime.Now));
        return File(file, StatementTemplate.ContentType, "RandAhead_income_statement_template.xlsx");
    }

    [HttpPost("preview")]
    [RequestSizeLimit(MaxFileBytes + 64 * 1024)]
    public async Task<IActionResult> Preview(IFormFile? file, CancellationToken cancellationToken)
    {
        var caller = Caller;
        var extension = Path.GetExtension(file?.FileName ?? "").ToLowerInvariant();
        var problem = true switch
        {
            _ when file is null || file.Length == 0 => "Choose a file to upload.",
            _ when extension == ".xls" => "Old .xls files can't be read. Open the file in Excel and save it as .xlsx (or .csv).",
            _ when extension is not (".xlsx" or ".csv") => "Please choose an Excel (.xlsx) or CSV file.",
            _ when file.Length > MaxFileBytes => "That file is over 32 MB. Remove extra sheets or images and try again.",
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
            var preview = StatementImporter.Read(stream, Path.GetFileName(file.FileName), remembered, caller.Unit!.Business!.YearEndMonth);
            return Ok(await lines.ApplyAsync(preview, caller, cancellationToken));
        }
        catch (StatementImportException error)
        {
            return BadRequest(new { message = error.Message });
        }
    }

    [HttpPost("check")]
    public IActionResult Check(SubmissionRequest request)
    {
        if (Problem(request) is { } problem)
        {
            return BadRequest(new { message = problem });
        }
        var rows = request.Rows!.Select(r => (r.Kind!, r.Subtotal, r.Values!, (IEnumerable<MappingPart>?)r.Mapping)).ToList();
        return Ok(IncomeStatement.FromRows(request.Years!, rows, request.Scale));
    }

    private static (string, string?, decimal?[], IEnumerable<MappingPart>?) StatementRow(SubmissionRowView r) =>
        (r.Kind, r.Subtotal, r.Values, r.Mapping);

    [HttpPost]
    public async Task<IActionResult> Create(SubmissionRequest request, CancellationToken cancellationToken)
    {
        var caller = Caller;
        var problem = Problem(request)
            ?? (request.Rows!.Any(r => r.Kind == "line" && r.Mapping!.Any(p => p.Category == IncomeStatement.Revenue))
                ? null
                : "No line is matched to Revenue. Every income statement needs sales or revenue.");
        if (problem is not null)
        {
            return BadRequest(new { message = problem });
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await scope.LockUnitAsync(caller.UnitId, cancellationToken);
        var submissions = await LoadAsync(caller.UnitId, tracked: true, cancellationToken);

        var yearEndMonth = caller.Unit!.Business!.YearEndMonth;
        var order = Enumerable.Range(0, request.Years!.Length).OrderBy(i => request.Years[i], StringComparer.Ordinal).ToArray();
        var starts = order.Select(i => FinancialYear.Start(request.Years![i], yearEndMonth)).ToArray();
        var rows = request.Rows!;
        var submission = new BudgetSubmission
        {
            UnitId = caller.UnitId,
            SubmittedBy = caller.UserId,
            Uploader = caller,
            PeriodStart = starts[0],
            PeriodEnd = starts[^1].AddYears(1).AddDays(-1),
            Status = "active",
            SubmittedAt = DateTime.UtcNow,
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

        var lines = rows
            .Where(r => r.Kind == "line")
            .Select(r => (order.Select(j => r.Values![j]).ToArray(), (IEnumerable<MappingPart>)(r.Mapping ?? [])));
        foreach (var (category, amounts) in IncomeStatement.CategoryTotals(lines, order.Length, request.Scale))
        {
            for (var k = 0; k < starts.Length; k++)
            {
                submission.LineItems.Add(
                    new FinancialLineItem { Category = category, PeriodStart = starts[k], Amount = Math.Round(amounts[k], 2) }
                );
            }
        }

        await RememberMappingsAsync(caller, rows, cancellationToken);
        db.BudgetSubmissions.Add(submission);
        var newestFirst = submissions.Prepend(submission).ToList();
        SupersedeCovered(newestFirst);
        var sorted = request.Years!.Order(StringComparer.Ordinal).ToArray();
        var span = sorted.Length == 1 ? sorted[0] : $"{sorted[0]}–{sorted[^1]}";
        audit.Add(caller, "upload", "uploaded", $"Uploaded {submission.SourceFileName} ({span})");
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(View(submission, newestFirst, yearEndMonth));
    }

    private async Task<List<BudgetSubmission>> LoadAsync(string unitId, bool tracked, CancellationToken cancellationToken)
    {
        var query = db
            .BudgetSubmissions.Where(s => s.UnitId == unitId)
            .Include(s => s.Uploader)
            .Include(s => s.Rows)
            .Include(s => s.LineItems)
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

    internal static void SupersedeCovered(List<BudgetSubmission> newestFirst)
    {
        var active = newestFirst.Where(s => s.Status == "active").ToList();
        var covered = new HashSet<DateOnly>();
        foreach (var submission in active)
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
            DisplayText.Person(s.Uploader),
            DisplayText.Day(s.SubmittedAt),
            s.Status,
            s.Status == "superseded" ? ReplacedBy(s, periods, all) : null,
            rows is null ? null : IncomeStatement.FromRows(labels, [.. rows.Select(StatementRow)], s.SourceScale)
        );
    }

    private static string? ReplacedBy(BudgetSubmission s, List<DateOnly> periods, List<BudgetSubmission> newestFirst) =>
        newestFirst
            .LastOrDefault(x => x.SubmittedAt > s.SubmittedAt && Periods(x).Intersect(periods).Any())
            ?.SubmissionId;

    private static List<DateOnly> Periods(BudgetSubmission s) =>
        s.LineItems.Select(i => i.PeriodStart).Distinct().Order().ToList();

    private static string? Problem(SubmissionRequest request)
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
            _ when years.Any(y => y is null || !YearLabel().IsMatch(y) || int.Parse(y[2..]) is < 1990 or > 2100) =>
                "Year headings must look like FY2025, between FY1990 and FY2100.",
            _ when years.Distinct().Count() != years.Length => "Each financial year can appear only once.",
            _ when rows.Length is 0 or > MaxRows => $"The statement needs 1 to {MaxRows} rows.",
            _ when rows.Any(r => r is null) => "Every row needs a label and amounts.",
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
                _ when row.Values!.Any(v => v is { } amount && Math.Abs(amount) > MaxRandAmount / request.Scale) =>
                    $"\"{label}\" has an amount over R10 trillion. Check the figures and the scale.",
                _ when row.Kind == "subtotal" && (row.Subtotal is null || !IncomeStatement.IsSubtotal(row.Subtotal)) => $"\"{label}\" is not a known subtotal.",
                _ when row.Kind == "line" && row.Mapping is null => $"\"{label}\" needs a category.",
                _ => null,
            };
            if (rowProblem is not null)
            {
                return rowProblem;
            }
            if (row.Kind == "line" && MappingProblem(label, row.Mapping!) is { } mappingProblem)
            {
                return mappingProblem;
            }
        }
        return null;
    }

    private static string? MappingProblem(string label, MappingPart[] parts) =>
        true switch
        {
            _ when parts.Any(p => p is null || !IncomeStatement.Categories.Contains(p.Category)) => $"\"{label}\" is matched to an unknown category.",
            _ when parts.Any(p => p.Percent is <= 0 or > 100) => $"Each share of \"{label}\" must be above 0% and at most 100%.",
            _ when parts.Any(p => p.Percent != Math.Round(p.Percent, 2)) => $"Each share of \"{label}\" can have at most 2 decimals.",
            _ when parts.Select(p => p.Category).Distinct().Count() != parts.Length => $"\"{label}\" lists a category twice.",
            _ when parts.Length > 0 && parts.Sum(p => p.Percent) != 100 => $"The shares of \"{label}\" must add up to 100%.",
            _ => null,
        };

    private static string CleanLabel(string? label) => Spaces().Replace(label?.Trim() ?? "", " ");

    private static string LabelKey(string label) => StatementImporter.LabelKey(label);
}
