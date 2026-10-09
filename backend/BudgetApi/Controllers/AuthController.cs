// Auth API: registers a new business with its first user, signs users in (logging each attempt), resets and changes passwords and returns who is signed in
using System.Buffers.Text;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using BudgetApi.Data;
using BudgetApi.Models;
using BudgetApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BudgetApi.Controllers;

public record LoginRequest([Required] string Email, [Required] string Password);

public record RegisterRequest(
    [property: JsonPropertyName("business_name")] string? BusinessName,
    [property: JsonPropertyName("industry")] string? Industry,
    [property: JsonPropertyName("year_end_month")] int? YearEndMonth,
    [property: JsonPropertyName("full_name")] string? FullName,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("password")] string? Password
);

public record ForgotPasswordRequest([property: JsonPropertyName("email")] string? Email);

public record ResetPasswordRequest(
    [property: JsonPropertyName("token")] string? Token,
    [property: JsonPropertyName("password")] string? Password
);

public record ChangePasswordRequest(
    [property: JsonPropertyName("current_password")] string? CurrentPassword,
    [property: JsonPropertyName("new_password")] string? NewPassword
);

[ApiController]
[Route("api/[controller]")]
public class AuthController(BudgetDbContext db, TokenService tokens, AuditLog audit, Mailer mailer) : ControllerBase
{
    private const int ResetMinutes = 30;

    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.Register)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var businessName = request.BusinessName?.Trim() ?? "";
        var industry = AccountRules.CleanIndustry(request.Industry);
        var fullName = AccountRules.CleanName(request.FullName);
        var problem =
            AccountRules.BusinessProblem(businessName, industry, request.YearEndMonth)
            ?? AccountRules.PersonProblem(fullName, request.Email, request.Password, passwordRequired: true);
        if (problem is not null)
        {
            return BadRequest(new { message = problem });
        }

        var email = request.Email!.Trim().ToLowerInvariant();
        if (await db.AppUsers.AnyAsync(u => u.Email.ToLower() == email && u.DeletedAt == null, cancellationToken))
        {
            return EmailConflict(email);
        }

        var business = new Business
        {
            LegalName = businessName,
            Industry = industry,
            YearEndMonth = (short)request.YearEndMonth!.Value,
        };
        var unit = new BusinessUnit { BusinessId = business.BusinessId, Business = business, UnitName = "Whole company" };
        var user = new AppUser
        {
            UnitId = unit.UnitId,
            Unit = unit,
            FullName = fullName,
            Email = email,
            Role = "decision_maker",
            PasswordHash = AccountRules.HashPassword(request.Password!),
        };

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            db.AppUsers.Add(user);
            await db.SaveChangesAsync(cancellationToken);
            db.RuleDefinitions.AddRange(DefaultRules.For(business.BusinessId));
            audit.Add(user, "account", "registered", $"Registered {business.LegalName} and became its Decision Maker");
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return EmailConflict(email);
        }

        var (token, expiresAt) = tokens.Create(user);
        return StatusCode(StatusCodes.Status201Created, new { token, expiresAt, user = UserView(user) });
    }

    private static readonly Lazy<string> DummyHash = new(() => BCrypt.Net.BCrypt.HashPassword("not-a-real-password", 11));

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.Login)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db
            .AppUsers.AsNoTracking()
            .Include(u => u.Unit)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email && u.DeletedAt == null, cancellationToken);

        var passwordOk = BCrypt.Net.BCrypt.Verify(request.Password, user?.PasswordHash ?? DummyHash.Value);
        if (user?.PasswordHash is null || !passwordOk)
        {
            if (user is not null)
            {
                audit.Add(user, "account", "sign_in_failed", "Sign-in failed: wrong password");
                await db.SaveChangesAsync(cancellationToken);
            }
            return Unauthorized(new { message = "Incorrect email or password." });
        }

        if (user.ActiveStatus != "active")
        {
            audit.Add(user, "account", "sign_in_failed", "Sign-in refused: the account is switched off");
            await db.SaveChangesAsync(cancellationToken);
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new { message = "This account is switched off. Ask your Approver or Decision Maker to switch it back on." }
            );
        }

        audit.Add(user, "account", "signed_in", "Signed in");
        await db.SaveChangesAsync(cancellationToken);
        var (token, expiresAt) = tokens.Create(user);
        return Ok(new { token, expiresAt, user = UserView(user) });
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.PasswordReset)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email?.Trim().ToLowerInvariant() ?? "";
        if (email.Length == 0)
        {
            return BadRequest(new { message = "Type your email address." });
        }
        var sent = new
        {
            message = $"If {email} has an account, we've emailed it a link to choose a new password. The link works for {ResetMinutes} minutes.",
        };
        var user = await db
            .AppUsers.Include(u => u.Unit)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email && u.DeletedAt == null, cancellationToken);
        if (user is null || user.ActiveStatus != "active")
        {
            return Ok(sent);
        }

        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var now = DateTime.UtcNow;
        await db.PasswordResets.Where(r => r.UserId == user.UserId && r.UsedAt == null).ExecuteDeleteAsync(cancellationToken);
        db.PasswordResets.Add(
            new PasswordReset
            {
                UserId = user.UserId,
                TokenHash = TokenHash(token),
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(ResetMinutes),
            }
        );
        audit.Add(user, "account", "reset_requested", "Asked for a password reset link");
        await db.SaveChangesAsync(cancellationToken);

        var body = $"""
            Hi {user.FullName},

            Someone (hopefully you) asked to reset the password for {user.Email} on RandAhead.

            Choose a new password here. The link works once, for {ResetMinutes} minutes:
            {mailer.WebAppUrl}/password.html?token={token}

            If you didn't ask for this, ignore this email and your password stays the same.
            """;
        await mailer.SendAsync(user.Email, "Reset your RandAhead password", body, cancellationToken);
        return Ok(sent);
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.PasswordReset)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        if (AccountRules.NewPasswordProblem(request.Password) is { } problem)
        {
            return BadRequest(new { message = problem });
        }
        var hash = TokenHash(request.Token?.Trim() ?? "");
        var reset = await db
            .PasswordResets.Include(r => r.User)
            .ThenInclude(u => u!.Unit)
            .FirstOrDefaultAsync(r => r.TokenHash == hash, cancellationToken);
        var now = DateTime.UtcNow;
        if (
            reset?.User is not { } user
            || reset.UsedAt is not null
            || reset.ExpiresAt <= now
            || user.DeletedAt is not null
            || user.ActiveStatus != "active"
        )
        {
            return BadRequest(new { message = "This reset link has expired or was already used. Ask for a new one." });
        }

        user.PasswordHash = AccountRules.HashPassword(request.Password!);
        user.MustChangePassword = false;
        reset.UsedAt = now;
        audit.Add(user, "account", "changed", "Reset their password with an emailed link");
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { message = "Your password is changed. Sign in with the new one." });
    }

    [HttpPost("change-password")]
    [Authorize]
    [EnableRateLimiting(RateLimits.Login)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var userId = User.FindFirst("sub")?.Value;
        var user = await db.AppUsers.Include(u => u.Unit).FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);
        if (user is null || user.ActiveStatus != "active" || user.DeletedAt is not null)
        {
            return Unauthorized(new { message = "Your session has ended. Sign in again." });
        }
        if (string.IsNullOrEmpty(request.CurrentPassword))
        {
            return BadRequest(new { message = "Type your current password." });
        }
        if (user.PasswordHash is not { } current || !BCrypt.Net.BCrypt.Verify(request.CurrentPassword, current))
        {
            return BadRequest(new { message = "Your current password is incorrect." });
        }
        if (AccountRules.NewPasswordProblem(request.NewPassword) is { } problem)
        {
            return BadRequest(new { message = problem });
        }
        if (request.NewPassword == request.CurrentPassword)
        {
            return BadRequest(new { message = "Choose a new password that's different from the current one." });
        }

        user.PasswordHash = AccountRules.HashPassword(request.NewPassword!);
        user.MustChangePassword = false;
        audit.Add(user, "account", "changed", "Changed their password");
        await db.SaveChangesAsync(cancellationToken);
        return Ok(UserView(user));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var userId = User.FindFirst("sub")?.Value;
        var user = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);
        if (user is null || user.ActiveStatus != "active")
        {
            return Unauthorized(new { message = "Your session has ended. Sign in again." });
        }
        return Ok(UserView(user));
    }

    private ConflictObjectResult EmailConflict(string email) =>
        Conflict(new { message = $"{email} already has an account. Sign in instead, or use another email address." });

    private static object UserView(AppUser user) =>
        new
        {
            id = user.UserId,
            user.FullName,
            user.Email,
            user.Role,
            user.UnitId,
            user.IsAdmin,
            user.MustChangePassword,
        };

    private static string TokenHash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
