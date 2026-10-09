// Ingestion service: fetches, validates and saves indicators
using BudgetApi.Data;
using BudgetApi.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetApi.Services;

public record IngestResult(
    string IndicatorCode,
    string SourceId,
    int IndicatorsSaved,
    int AlertsCreated,
    IReadOnlyList<ExternalIndicator> LatestIndicators,
    IReadOnlyList<RuleAlert> Alerts
);

public record IndicatorPoint(string CountryCode, int Year, decimal Value);

public class IngestionService(
    BudgetDbContext db,
    WorldBankClient worldBankClient,
    OecdClient oecdClient,
    RuleEngine ruleEngine,
    ILogger<IngestionService> logger
)
{
    public async Task<IReadOnlyList<IngestResult>> IngestAllAsync(
        CancellationToken cancellationToken = default
    )
    {
        var results = new List<IngestResult>();

        foreach (var definition in IndicatorCatalog.All)
        {
            results.Add(await IngestIndicatorAsync(definition, cancellationToken));
        }

        return results;
    }

    public async Task<IngestResult> IngestIndicatorAsync(
        IndicatorDefinition definition,
        CancellationToken cancellationToken = default
    )
    {
        var indicatorType =
            await db.IndicatorTypes.FirstOrDefaultAsync(
                t => t.IndicatorCode == definition.Code,
                cancellationToken
            )
            ?? throw new InvalidOperationException(
                $"Indicator '{definition.Code}' is missing from indicator_type. Check 02_reference_data.sql."
            );

        var points = await FetchAsync(definition, cancellationToken);

        var source = await db.DataSources.FirstOrDefaultAsync(
            s => s.SourceIdentifier == definition.SourceIdentifier,
            cancellationToken
        );

        if (source is null)
        {
            source = new DataSource
            {
                SourceName = definition.SourceName,
                SourceIdentifier = definition.SourceIdentifier,
                SourceType = "api",
                TrustedStatus = "trusted",
            };
            db.DataSources.Add(source);
            await db.SaveChangesAsync(cancellationToken);
        }

        var savedIndicators = new List<ExternalIndicator>();

        foreach (var point in points)
        {
            var period = point.Year.ToString();

            var indicator = await db.ExternalIndicators.FirstOrDefaultAsync(
                i =>
                    i.SourceId == source.SourceId
                    && i.IndicatorCode == definition.Code
                    && i.RelevantPeriod == period,
                cancellationToken
            );

            if (indicator is null)
            {
                indicator = new ExternalIndicator
                {
                    SourceId = source.SourceId,
                    IndicatorCode = definition.Code,
                    RelevantPeriod = period,
                };
                db.ExternalIndicators.Add(indicator);
            }

            indicator.IndicatorName = indicatorType.DisplayName;
            indicator.IndicatorValue = point.Value;
            indicator.MeasurementUnit = indicatorType.MeasurementUnit;
            indicator.ValidationStatus = Validate(point);
            indicator.CollectedAt = DateTime.UtcNow;
            savedIndicators.Add(indicator);
        }

        await db.SaveChangesAsync(cancellationToken);

        var alerts = await ruleEngine.EvaluateIndicatorRulesAsync(
            definition.Code,
            savedIndicators,
            cancellationToken
        );

        logger.LogInformation(
            "Ingest complete. Indicator={Code}, source={SourceId}, indicators={Count}, alerts={AlertCount}",
            definition.Code,
            source.SourceId,
            savedIndicators.Count,
            alerts.Count
        );

        return new IngestResult(
            definition.Code,
            source.SourceId,
            savedIndicators.Count,
            alerts.Count,
            savedIndicators.OrderByDescending(i => i.RelevantPeriod).Take(5).ToList(),
            alerts
        );
    }

    private Task<IReadOnlyList<IndicatorPoint>> FetchAsync(
        IndicatorDefinition definition,
        CancellationToken cancellationToken
    ) =>
        definition.Provider switch
        {
            IndicatorProvider.WorldBank => worldBankClient.FetchIndicatorAsync(
                definition.SeriesKey,
                cancellationToken: cancellationToken
            ),
            IndicatorProvider.Oecd => oecdClient.FetchAnnualInflationAsync(
                definition.SeriesKey,
                cancellationToken: cancellationToken
            ),
            _ => throw new NotSupportedException($"Unknown provider {definition.Provider}."),
        };

    private static string Validate(IndicatorPoint point) =>
        point.CountryCode != IndicatorCatalog.CountryCode
        || point.Year < 1990
        || point.Year > DateTime.UtcNow.Year
            ? "rejected"
        : point.Value < -50m || point.Value > 100m ? "flagged"
        : "validated";
}
