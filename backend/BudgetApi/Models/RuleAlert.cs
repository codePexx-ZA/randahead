// Rule alert model: a warning raised by a rule
using System.Text.Json.Serialization;

namespace BudgetApi.Models;

public class RuleAlert
{
    public string AlertId { get; set; } = Guid.NewGuid().ToString("N");
    public string RuleId { get; set; } = string.Empty;
    public string? UnitId { get; set; }
    public string? RunId { get; set; }
    public string Severity { get; set; } = "info";
    public string Message { get; set; } = string.Empty;
    public string ResolutionStatus { get; set; } = "open";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public RuleDefinition? Rule { get; set; }
}
