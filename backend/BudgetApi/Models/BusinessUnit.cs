// Business unit model: the part of a business that budgets (for now the whole company)
namespace BudgetApi.Models;

public class BusinessUnit
{
    public string UnitId { get; set; } = Guid.NewGuid().ToString("N");
    public string BusinessId { get; set; } = string.Empty;
    public string UnitName { get; set; } = string.Empty;
    public string UnitType { get; set; } = "company";
    public string Currency { get; set; } = "ZAR";

    public Business? Business { get; set; }
}
