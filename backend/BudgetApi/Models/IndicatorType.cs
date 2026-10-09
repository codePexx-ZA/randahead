// Indicator type model: a tracked economic indicator
using System.Text.Json.Serialization;

namespace BudgetApi.Models;

public class IndicatorType
{
    public string IndicatorCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string MeasurementUnit { get; set; } = "percent";
    public string OfficialSource { get; set; } = string.Empty;

    [JsonIgnore]
    public ICollection<IndicatorCategoryLink> CategoryLinks { get; set; } =
        new List<IndicatorCategoryLink>();
}
