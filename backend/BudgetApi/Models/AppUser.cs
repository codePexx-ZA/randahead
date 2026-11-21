using System.Text.Json.Serialization;

namespace BudgetApi.Models;

public class AppUser
{
    public string UserId { get; set; } = Guid.NewGuid().ToString("N");
    public string UnitId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;

    [JsonIgnore]
    public string PasswordHash { get; set; } = string.Empty;

    [JsonIgnore]
    public BusinessUnit? Unit { get; set; }
}
