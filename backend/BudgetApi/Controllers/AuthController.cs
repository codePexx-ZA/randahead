using System.ComponentModel.DataAnnotations;
using BudgetApi.Data;
using BudgetApi.Models;
using BudgetApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace BudgetApi.Controllers;

public record LoginRequest([Required] string Username, [Required] string Password);

[ApiController]
[Route("api/[controller]")]
public class AuthController(BudgetDbContext db, TokenService tokens) : ApiController
{
    private static readonly Lazy<string> DummyHash = new(() => BCrypt.Net.BCrypt.HashPassword("not-a-real-password", 11));

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.Login)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var username = request.Username.Trim().ToLowerInvariant();
        await RlsSession.SetLoginAsync(db, username, cancellationToken);
        var user = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Username == username, cancellationToken);

        var passwordOk = BCrypt.Net.BCrypt.Verify(request.Password, user?.PasswordHash ?? DummyHash.Value);
        if (user is null || !passwordOk)
        {
            return Unauthorized(new { message = "Incorrect username or password." });
        }

        var (token, expiresAt) = tokens.Create(user);
        return Ok(new { token, expiresAt, user = UserView(user) });
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me() => Ok(UserView(Caller));

    private static object UserView(AppUser user) =>
        new
        {
            id = user.UserId,
            user.FullName,
            user.Username,
            user.UnitId,
        };
}
