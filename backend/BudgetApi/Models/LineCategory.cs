// Line category model: an income statement category
namespace BudgetApi.Models;

public class LineCategory
{
    public string CategoryCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public short SortOrder { get; set; }
    public bool TeamManaged { get; set; }
}
