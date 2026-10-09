// OECD client: fetches inflation data from the OECD API
using System.Globalization;

namespace BudgetApi.Services;

public class OecdClient(HttpClient httpClient, ILogger<OecdClient> logger)
{
    private const string BaseUrl =
        "https://sdmx.oecd.org/public/rest/data/OECD.SDD.TPS,DSD_PRICES@DF_PRICES_ALL,1.0";

    public async Task<IReadOnlyList<IndicatorPoint>> FetchAnnualInflationAsync(
        string seriesKey,
        string countryCode = IndicatorCatalog.CountryCode,
        int years = IndicatorCatalog.HistoryYears,
        CancellationToken cancellationToken = default
    )
    {
        var url =
            $"{BaseUrl}/{countryCode}.A.N.CPI.PA.{seriesKey}.N.GY?startPeriod={DateTime.UtcNow.Year - years}";

        logger.LogInformation("Fetching OECD data from {Url}", url);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Accept", "application/vnd.sdmx.data+csv; charset=utf-8");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var csv = await response.Content.ReadAsStringAsync(cancellationToken);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length == 0)
        {
            return [];
        }

        var header = lines[0].Split(',');
        var areaIndex = Array.IndexOf(header, "REF_AREA");
        var periodIndex = Array.IndexOf(header, "TIME_PERIOD");
        var valueIndex = Array.IndexOf(header, "OBS_VALUE");

        if (areaIndex < 0 || periodIndex < 0 || valueIndex < 0)
        {
            throw new InvalidOperationException("Unexpected OECD response shape.");
        }

        var results = new List<IndicatorPoint>();

        foreach (var fields in lines.Skip(1).Select(line => line.Split(',')))
        {
            if (
                fields.Length > valueIndex
                && int.TryParse(fields[periodIndex], out var year)
                && decimal.TryParse(
                    fields[valueIndex],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value
                )
                && string.Equals(fields[areaIndex], countryCode, StringComparison.OrdinalIgnoreCase)
            )
            {
                results.Add(new IndicatorPoint(fields[areaIndex], year, value));
            }
        }

        logger.LogInformation(
            "Parsed {Count} OECD points for {Country}/{Series}",
            results.Count,
            countryCode,
            seriesKey
        );

        return results.OrderByDescending(p => p.Year).ToList();
    }
}
