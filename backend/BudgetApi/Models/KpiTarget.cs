// KPI target model: a unit's goal for one KPI in one financial year
namespace BudgetApi.Models;

public class KpiTarget
{
    public string TargetId { get; set; } = Guid.NewGuid().ToString("N");
    public string UnitId { get; set; } = string.Empty;
    public string MetricName { get; set; } = string.Empty;
    public decimal TargetValue { get; set; }
    public string MeasurementUnit { get; set; } = string.Empty;
    public DateOnly TargetPeriod { get; set; }
}
