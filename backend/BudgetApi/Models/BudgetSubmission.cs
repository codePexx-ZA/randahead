// Budget submission model: one uploaded income statement file and where it is in the approval flow
namespace BudgetApi.Models;

public class BudgetSubmission
{
    public string SubmissionId { get; set; } = Guid.NewGuid().ToString("N");
    public string UnitId { get; set; } = string.Empty;
    public string SubmittedBy { get; set; } = string.Empty;
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public string Status { get; set; } = "draft";
    public DateTime? SubmittedAt { get; set; }
    public string? ReplacesSubmissionId { get; set; }
    public string ImportMethod { get; set; } = "template";
    public string? SourceFileName { get; set; }
    public int SourceScale { get; set; } = 1;

    public AppUser? Submitter { get; set; }
    public ICollection<FinancialLineItem> LineItems { get; set; } = new List<FinancialLineItem>();
    public ICollection<SubmissionRow> Rows { get; set; } = new List<SubmissionRow>();
    public ICollection<ApprovalAction> Approvals { get; set; } = new List<ApprovalAction>();
}
