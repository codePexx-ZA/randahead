namespace BudgetApi.Models;

public class ApprovalAction
{
    public string ApprovalId { get; set; } = Guid.NewGuid().ToString("N");
    public string? SubmissionId { get; set; }
    public string? BudgetId { get; set; }
    public string ActedBy { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;
    public string? Comments { get; set; }
    public DateTime ActedAt { get; set; } = DateTime.UtcNow;

    public AppUser? Actor { get; set; }
}
