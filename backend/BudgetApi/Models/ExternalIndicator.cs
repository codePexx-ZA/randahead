// External indicator model: one indicator value per year
using System.Text.Json.Serialization;

namespace BudgetApi.Models;

public class ExternalIndicator
{
    public string IndicatorId { get; set; } = Guid.NewGuid().ToString("N");
    public string SourceId { get; set; } = string.Empty;
    public string IndicatorCode { get; set; } = string.Empty;
    public string IndicatorName { get; set; } = string.Empty;
    public decimal IndicatorValue { get; set; }
    public string MeasurementUnit { get; set; } = string.Empty;
    public string RelevantPeriod { get; set; } = string.Empty;
    public string ValidationStatus { get; set; } = "pending";
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public DataSource? Source { get; set; }
}
