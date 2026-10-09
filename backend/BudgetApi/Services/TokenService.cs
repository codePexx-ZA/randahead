// Token service: creates signed JWT sign-in tokens for users
using System.Security.Claims;
using System.Text;
using BudgetApi.Models;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BudgetApi.Services;

public class JwtSettings
{
    public string Issuer { get; set; } = "RandAhead";
    public string Audience { get; set; } = "RandAhead";
    public string Key { get; set; } = string.Empty;
    public int ExpiryHours { get; set; } = 8;

    public SymmetricSecurityKey SigningKey() => new(Encoding.UTF8.GetBytes(Key));
}

public class TokenService(JwtSettings settings)
{
    public (string Token, DateTime ExpiresAt) Create(AppUser user)
    {
        var expiresAt = DateTime.UtcNow.AddHours(settings.ExpiryHours);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            Expires = expiresAt,
            SigningCredentials = new SigningCredentials(settings.SigningKey(), SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.UserId),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim("name", user.FullName),
                new Claim("role", user.Role),
                new Claim("unit_id", user.UnitId),
            ]),
        };
        return (new JsonWebTokenHandler().CreateToken(descriptor), expiresAt);
    }
}
