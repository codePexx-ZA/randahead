// Audit entry model: one thing a person did in the business, with what changed and when
namespace BudgetApi.Models;

public class AuditEntry
{
    public string LogId { get; set; } = Guid.NewGuid().ToString("N");
    public string BusinessId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? Details { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    public AppUser? User { get; set; }
}
