using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BudgetApi.Data;
using Microsoft.EntityFrameworkCore;
using BudgetApi.Models;

namespace BudgetApi.Services;

/// <summary>
/// Sends only account labels the importer could not categorise to DeepSeek V4.1 Flash.
/// Amounts, file names and business names are not included.
/// </summary>
public class LineCategoriser(HttpClient http, IConfiguration configuration, BudgetDbContext db, AuditLog audit)
{
    public const string Model = "deepseek-flash";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<StatementPreview> ApplyAsync(StatementPreview preview, AppUser actor, CancellationToken cancellationToken)
    {
        var pending = preview.Rows.Where(row => row.Kind == "line" && row.Mapping is null).Select(row => row.Label).Distinct().ToArray();
        var key = configuration["DeepSeek:ApiKey"];
        if (pending.Length == 0 || string.IsNullOrWhiteSpace(key))
        {
            return preview with { AiRan = false };
        }

        Dictionary<string, string> chosen;
        try
        {
            chosen = await ClassifyAsync(key, pending, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return preview with { AiRan = false };
        }

        var applied = new List<(string Label, string Category)>();
        var rows = preview.Rows.Select(row =>
        {
            if (row.Kind != "line" || row.Mapping is not null)
            {
                return row;
            }
            if (!chosen.TryGetValue(StatementImporter.LabelKey(row.Label), out var category))
            {
                return row;
            }
            applied.Add((row.Label, category));
            return row with
            {
                Status = "auto",
                Mapping = new PreviewMapping("single", Category: category),
                Candidates = null,
            };
        }).ToArray();

        await RecordAsync(actor, applied, cancellationToken);
        var statement = rows.Any(row => row.Kind == "line" && row.Mapping is null)
            ? null
            : IncomeStatement.FromRows(preview.Years, [.. rows.Select(row => (row.Kind, row.Subtotal, row.Values, Parts(row.Mapping)))], preview.Scale);
        return preview with { Rows = rows, Statement = statement, AiRan = true };
    }

    internal static Dictionary<string, string> ReadChoices(string body)
    {
        using var document = JsonDocument.Parse(StripFence(body));
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("items", out var items))
        {
            root = items;
        }
        var chosen = new Dictionary<string, string>();
        if (root.ValueKind != JsonValueKind.Array)
        {
            return chosen;
        }
        foreach (var item in root.EnumerateArray())
        {
            if (!item.TryGetProperty("label", out var labelProp) || !item.TryGetProperty("category", out var categoryProp))
            {
                continue;
            }
            var label = labelProp.GetString();
            var category = categoryProp.GetString()?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(label) || category is null || !IncomeStatement.Categories.Contains(category))
            {
                continue;
            }
            chosen[StatementImporter.LabelKey(label)] = category;
        }
        return chosen;
    }

    private async Task<Dictionary<string, string>> ClassifyAsync(string key, IReadOnlyList<string> labels, CancellationToken cancellationToken)
    {
        var categories = string.Join(", ", IncomeStatement.Categories);
        var payload = JsonSerializer.Serialize(new
        {
            model = Model,
            thinking = new { type = "disabled" },
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "Classify each income-statement account label into one RandAhead category. "
                        + $"Categories: {categories}. "
                        + "Reply with JSON {\"items\":[{\"label\":\"...\",\"category\":\"...\"}]} using the label text unchanged. "
                        + "Use a category only when the label clearly belongs there. No amounts are included and none should be assumed.",
                },
                new { role = "user", content = JsonSerializer.Serialize(new { labels }) },
            },
        }, Json);

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(body);
        var content = document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
            ?? throw new JsonException("DeepSeek returned no classification.");
        return ReadChoices(content);
    }

    private async Task RecordAsync(AppUser actor, List<(string Label, string Category)> applied, CancellationToken cancellationToken)
    {
        if (applied.Count == 0)
        {
            return;
        }
        var businessId = actor.Unit!.BusinessId;
        var seen = await db.AuditEntries.AsNoTracking()
            .Where(entry => entry.BusinessId == businessId && entry.Area == "ai" && entry.Action == "categorised")
            .Select(entry => entry.Summary)
            .ToListAsync(cancellationToken);
        var known = seen.ToHashSet();
        foreach (var (label, category) in applied.DistinctBy(item => StatementImporter.LabelKey(item.Label)))
        {
            var summary = $"{label} → {CategoryName(category)}";
            if (!known.Add(summary))
            {
                continue;
            }
            audit.Add(actor, "ai", "categorised", summary, [new AuditChange("Category", null, CategoryName(category))]);
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    internal static string CategoryName(string category) => category switch
    {
        "revenue" => "Revenue",
        "other_income" => "Other income",
        "cost_of_sales" => "Cost of sales",
        "employee_costs" => "Employee costs",
        "occupancy" => "Occupancy",
        "electricity" => "Electricity",
        "other_utilities" => "Water, telecoms and other utilities",
        "transport_fuel" => "Transport and fuel",
        "marketing" => "Marketing",
        "other_operating" => "Other operating expenses",
        "depreciation" => "Depreciation and amortisation",
        "finance_costs" => "Finance costs",
        "income_tax" => "Income tax",
        _ => category,
    };

    private static IEnumerable<MappingPart>? Parts(PreviewMapping? mapping) => mapping?.Type switch
    {
        "single" => [new MappingPart(mapping.Category!, 100)],
        "split" => mapping.Parts,
        "ignore" => [],
        _ => null,
    };

    private static string StripFence(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }
        var lines = trimmed.Split('\n').Skip(1).ToList();
        if (lines.Count > 0 && lines[^1].TrimStart().StartsWith("```", StringComparison.Ordinal))
        {
            lines.RemoveAt(lines.Count - 1);
        }
        return string.Join('\n', lines).Trim();
    }
}
