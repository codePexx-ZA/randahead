// Rule definition model: a business's warning rule with a threshold
using System.Text.Json.Serialization;

namespace BudgetApi.Models;

public class RuleDefinition
{
    public string RuleId { get; set; } = Guid.NewGuid().ToString("N");
    public string BusinessId { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? IndicatorCode { get; set; }
    public decimal? ThresholdValue { get; set; }
    public string ActiveStatus { get; set; } = "active";

    [JsonIgnore]
    public ICollection<RuleAlert> Alerts { get; set; } = new List<RuleAlert>();
}
