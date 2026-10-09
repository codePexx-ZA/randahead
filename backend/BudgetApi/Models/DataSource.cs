// Data source model: where indicator data comes from
using System.Text.Json.Serialization;

namespace BudgetApi.Models;

public class DataSource
{
    public string SourceId { get; set; } = Guid.NewGuid().ToString("N");
    public string SourceName { get; set; } = string.Empty;
    public string SourceIdentifier { get; set; } = string.Empty;
    public string SourceType { get; set; } = "api";
    public string TrustedStatus { get; set; } = "trusted";

    [JsonIgnore]
    public ICollection<ExternalIndicator> Indicators { get; set; } = new List<ExternalIndicator>();
}
