// Indicator catalog: where each indicator is fetched from
namespace BudgetApi.Services;

public enum IndicatorProvider
{
    WorldBank,
    Oecd,
}

public record IndicatorDefinition(
    string Code,
    IndicatorProvider Provider,
    string SeriesKey,
    string SourceName,
    string SourceIdentifier
);

public static class IndicatorCatalog
{
    public const string CountryCode = "ZAF";

    public const int HistoryYears = 6;

    public static readonly IReadOnlyList<IndicatorDefinition> All =
    [
        new(
            "cpi_headline",
            IndicatorProvider.WorldBank,
            "FP.CPI.TOTL.ZG",
            "World Bank Open Data",
            "worldbank:FP.CPI.TOTL.ZG:ZAF"
        ),
        new(
            "cpi_core",
            IndicatorProvider.Oecd,
            "_TXCP01_NRG",
            "OECD Consumer Prices",
            "oecd:DF_PRICES_ALL:ZAF:_TXCP01_NRG"
        ),
        new(
            "electricity_price",
            IndicatorProvider.Oecd,
            "CP045",
            "OECD Consumer Prices",
            "oecd:DF_PRICES_ALL:ZAF:CP045"
        ),
        new(
            "fuel_price",
            IndicatorProvider.Oecd,
            "CP0722",
            "OECD Consumer Prices",
            "oecd:DF_PRICES_ALL:ZAF:CP0722"
        ),
    ];

    public static IndicatorDefinition? Find(string code) =>
        All.FirstOrDefault(d => string.Equals(d.Code, code, StringComparison.OrdinalIgnoreCase));
}
