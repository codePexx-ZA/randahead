// Forecast run model: one method's forecast for a business unit
namespace BudgetApi.Models;

public class ForecastRun
{
    public string RunId { get; set; } = Guid.NewGuid().ToString("N");
    public string UnitId { get; set; } = string.Empty;
    public string ForecastingMethod { get; set; } = string.Empty;
    public DateOnly HistoryStart { get; set; }
    public DateOnly HistoryEnd { get; set; }
    public string Parameters { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }

    public ICollection<ForecastValue> Values { get; set; } = new List<ForecastValue>();
}
