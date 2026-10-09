// Budget selection model: the forecast a decision maker chose as the budget and where it is in the approval flow
namespace BudgetApi.Models;

public class BudgetSelection
{
    public string BudgetId { get; set; } = Guid.NewGuid().ToString("N");
    public string UnitId { get; set; } = string.Empty;
    public string MaRunId { get; set; } = string.Empty;
    public string EsRunId { get; set; } = string.Empty;
    public string BudgetMethod { get; set; } = string.Empty;
    public string Status { get; set; } = "submitted";
    public string ChosenBy { get; set; } = string.Empty;
    public DateTime ChosenAt { get; set; } = DateTime.UtcNow;

    public AppUser? Chooser { get; set; }
    public ForecastRun? MaRun { get; set; }
    public ForecastRun? EsRun { get; set; }
    public ICollection<ApprovalAction> Approvals { get; set; } = new List<ApprovalAction>();
}
