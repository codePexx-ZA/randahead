// App user model: a person who signs in, with one role
using System.Text.Json.Serialization;

namespace BudgetApi.Models;

public class AppUser
{
    public string UserId { get; set; } = Guid.NewGuid().ToString("N");
    public string UnitId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    [JsonIgnore]
    public string? PasswordHash { get; set; }
    public string Role { get; set; } = string.Empty;
    public string ActiveStatus { get; set; } = "active";
    public bool IsAdmin { get; set; }
    public bool MustChangePassword { get; set; }
    public DateTime? DeletedAt { get; set; }

    [JsonIgnore]
    public BusinessUnit? Unit { get; set; }
}
