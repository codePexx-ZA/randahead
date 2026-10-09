// Password reset model: a one-time emailed link, stored only as a hash of its token
namespace BudgetApi.Models;

public class PasswordReset
{
    public string ResetId { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }

    public AppUser? User { get; set; }
}
