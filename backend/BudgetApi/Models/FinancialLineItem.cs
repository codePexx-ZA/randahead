// Financial line item model: one category's amount in rands for one period of a submission
namespace BudgetApi.Models;

public class FinancialLineItem
{
    public string LineItemId { get; set; } = Guid.NewGuid().ToString("N");
    public string SubmissionId { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public DateOnly PeriodStart { get; set; }
    public decimal Amount { get; set; }
    public string Frequency { get; set; } = "annual";
}
