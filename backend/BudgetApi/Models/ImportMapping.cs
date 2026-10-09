// Import mapping model: a business's remembered choice of category (and split) for a file label
namespace BudgetApi.Models;

public class ImportMapping
{
    public string MappingId { get; set; } = Guid.NewGuid().ToString("N");
    public string BusinessId { get; set; } = string.Empty;
    public string SourceLabel { get; set; } = string.Empty;
    public string LabelKey { get; set; } = string.Empty;
    public string CategoryCode { get; set; } = string.Empty;
    public decimal SplitPercent { get; set; } = 100;
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
