using System.Net;
using System.Text;
using BudgetApi.Data;
using BudgetApi.Models;
using BudgetApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BudgetApi.Tests;

public class LineCategoriserTests
{
    [Fact]
    public void ReadChoices_KeepsOnlyKnownCategories()
    {
        var chosen = LineCategoriser.ReadChoices("""
            {"items":[
              {"label":"Zibble Fee","category":"other_operating"},
              {"label":"Mystery","category":"not_a_category"}
            ]}
            """);

        Assert.Equal("other_operating", chosen[StatementImporter.LabelKey("Zibble Fee")]);
        Assert.False(chosen.ContainsKey(StatementImporter.LabelKey("Mystery")));
    }

    [Fact]
    public void ImporterCovers_KnowsSalesAndNotAnUnseenName()
    {
        Assert.True(StatementImporter.ImporterCovers("Sales"));
        Assert.False(StatementImporter.ImporterCovers("Zibble Fee"));
    }

    [Fact]
    public void StillOpen_DropsNamesTheImporterHasLearned()
    {
        var open = AiImporterReport.StillOpen(
        [
            ("Zibble Fee", "Other operating expenses", DateTime.UtcNow),
            ("Sales", "Revenue", DateTime.UtcNow),
        ]);

        Assert.Equal([("Zibble Fee", "Other operating expenses")], open);
    }

    [Fact]
    public void MostRecentDue_IsSunday2359SouthAfricanTime()
    {
        var before = new DateTime(2026, 11, 22, 21, 58, 0, DateTimeKind.Utc);
        var onTime = new DateTime(2026, 11, 22, 21, 59, 0, DateTimeKind.Utc);

        Assert.Equal(new DateTime(2026, 11, 15, 21, 59, 0, DateTimeKind.Utc), AiImporterReport.MostRecentDueUtc(before));
        Assert.Equal(onTime, AiImporterReport.MostRecentDueUtc(onTime));
    }

    [Fact]
    public async Task Apply_FillsTheLabelAndRecordsItOnce()
    {
        await using var db = NewDb();
        var actor = Actor();
        var categoriser = NewCategoriser(db, """{"items":[{"label":"Zibble Fee","category":"other_operating"}]}""");
        var preview = Preview("Zibble Fee");

        var first = await categoriser.ApplyAsync(preview, actor, CancellationToken.None);
        var second = await categoriser.ApplyAsync(preview, actor, CancellationToken.None);

        Assert.True(first.AiRan);
        Assert.Equal("other_operating", first.Rows.Single(row => row.Kind == "line").Mapping!.Category);
        Assert.NotNull(first.Statement);
        Assert.Equal(1, await db.AuditEntries.CountAsync(entry => entry.Action == "categorised"));
        Assert.True(second.AiRan);
        Assert.Equal(1, await db.AuditEntries.CountAsync(entry => entry.Action == "categorised"));
    }

    [Fact]
    public async Task Apply_DoesNotCallTheModelWhenEveryLineIsKnown()
    {
        await using var db = NewDb();
        var categoriser = NewCategoriser(db, "should-not-be-called", called: _ => throw new InvalidOperationException("called"));
        var preview = Preview("Sales", new PreviewMapping("single", Category: "revenue"));

        var result = await categoriser.ApplyAsync(preview, Actor(), CancellationToken.None);

        Assert.False(result.AiRan);
    }

    private static LineCategoriser NewCategoriser(BudgetDbContext db, string content, Action<HttpRequestMessage>? called = null)
    {
        var handler = new StubHandler(content, called);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.deepseek.com/") };
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DeepSeek:ApiKey"] = "test-key" }).Build();
        return new LineCategoriser(client, config, db, new AuditLog(db));
    }

    private static StatementPreview Preview(string label, PreviewMapping? mapping = null)
    {
        var row = new PreviewRow(0, label, StatementImporter.LabelKey(label), [100m, 100m], "line", null, mapping is null ? "unknown" : "auto", mapping, null);
        return new StatementPreview("sheet.xlsx", "annual", ["FY2025", "FY2026"], 1, null, [row], [], null);
    }

    private static AppUser Actor() => new()
    {
        UserId = "user",
        FullName = "Tester",
        Unit = new BusinessUnit { BusinessId = "business" },
    };

    private static BudgetDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<BudgetDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new BudgetDbContext(options);
    }

    private sealed class StubHandler(string content, Action<HttpRequestMessage>? called) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            called?.Invoke(request);
            var body = "{\"choices\":[{\"message\":{\"content\":" + System.Text.Json.JsonSerializer.Serialize(content) + "}}]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
