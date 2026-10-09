// Indicator category link model: links indicators to categories
using System.Text.Json.Serialization;

namespace BudgetApi.Models;

public class IndicatorCategoryLink
{
    public string IndicatorCode { get; set; } = string.Empty;
    public string CategoryCode { get; set; } = string.Empty;

    [JsonIgnore]
    public IndicatorType? IndicatorType { get; set; }

    [JsonIgnore]
    public LineCategory? Category { get; set; }
}
