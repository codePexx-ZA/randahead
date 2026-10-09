// World Bank client: fetches inflation data from the World Bank API
using System.Text.Json;

namespace BudgetApi.Services;

public class WorldBankClient(HttpClient httpClient, ILogger<WorldBankClient> logger)
{
    public async Task<IReadOnlyList<IndicatorPoint>> FetchIndicatorAsync(
        string indicatorCode,
        string countryCode = IndicatorCatalog.CountryCode,
        int years = IndicatorCatalog.HistoryYears,
        CancellationToken cancellationToken = default
    )
    {
        var endYear = DateTime.UtcNow.Year;
        var url =
            $"https://api.worldbank.org/v2/country/{countryCode}/indicator/{indicatorCode}?format=json&date={endYear - years}:{endYear}&per_page=100";

        logger.LogInformation("Fetching World Bank data from {Url}", url);

        using var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken
        );

        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 2)
        {
            throw new InvalidOperationException("Unexpected World Bank response shape.");
        }

        if (root[1].ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        var results = new List<IndicatorPoint>();

        foreach (var row in root[1].EnumerateArray())
        {
            if (
                !row.TryGetProperty("value", out var valueElement)
                || valueElement.ValueKind == JsonValueKind.Null
                || !row.TryGetProperty("date", out var dateElement)
                || !int.TryParse(dateElement.GetString(), out var year)
            )
            {
                continue;
            }

            var country = row.GetProperty("countryiso3code").GetString() ?? countryCode;
            if (string.Equals(country, countryCode, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new IndicatorPoint(country, year, valueElement.GetDecimal()));
            }
        }

        logger.LogInformation(
            "Parsed {Count} World Bank points for {Country}/{Indicator}",
            results.Count,
            countryCode,
            indicatorCode
        );

        return results.OrderByDescending(p => p.Year).ToList();
    }
}
