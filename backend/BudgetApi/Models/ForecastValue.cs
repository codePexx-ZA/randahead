// Forecast value model: one category's forecast amount for one future period of a run
namespace BudgetApi.Models;

public class ForecastValue
{
    public string ValueId { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public DateOnly ForecastPeriod { get; set; }
    public decimal BaselineAmount { get; set; }
    public decimal? AdjustedAmount { get; set; }
}
