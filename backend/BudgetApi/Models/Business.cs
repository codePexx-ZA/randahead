// Business model: the company that uses the app
namespace BudgetApi.Models;

public class Business
{
    public string BusinessId { get; set; } = Guid.NewGuid().ToString("N");
    public string LegalName { get; set; } = string.Empty;
    public string? Industry { get; set; }
    public string Country { get; set; } = "ZAF";
    public short YearEndMonth { get; set; } = 2;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<BusinessUnit> Units { get; set; } = new List<BusinessUnit>();
}
