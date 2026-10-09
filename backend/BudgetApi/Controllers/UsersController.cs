// Users API: lists, adds, edits, switches off and deletes the people who sign in to the caller's business
using System.Text.Json.Serialization;
using BudgetApi.Data;
using BudgetApi.Models;
using BudgetApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BudgetApi.Controllers;

public record UserRequest(
    [property: JsonPropertyName("full_name")] string? FullName,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("role")] string? Role,
    [property: JsonPropertyName("active_status")] string? ActiveStatus,
    [property: JsonPropertyName("password")] string? Password
);

public record DeleteUserRequest([property: JsonPropertyName("password")] string? Password);

[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController(BudgetDbContext db, BusinessScope scope, AuditLog audit) : ControllerBase
{
    private const string UserManagers = "approver,decision_maker";
    private static readonly Dictionary<string, string> RoleNames = AuditLog.RoleNames;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (await scope.CallerAsync(User, cancellationToken) is not { } caller)
        {
            return SessionEnded();
        }
        var businessId = caller.Unit!.BusinessId;
        var users = await BusinessUsers(businessId).AsNoTracking().OrderBy(u => u.FullName).ToListAsync(cancellationToken);
        var onRecord = await scope.UsersOnRecordAsync(businessId, cancellationToken);
        return Ok(users.Select(u => UserView(u, !onRecord.Contains(u.UserId))));
    }

    [HttpPost]
    [Authorize(Roles = UserManagers)]
    public async Task<IActionResult> Create(UserRequest request, CancellationToken cancellationToken)
    {
        if (await scope.CallerAsync(User, cancellationToken) is not { } caller)
        {
            return SessionEnded();
        }
        if (Problem(request, passwordRequired: true) is { } problem)
        {
            return BadRequest(new { message = problem });
        }

        var email = request.Email!.Trim().ToLowerInvariant();
        if (await EmailTakenAsync(email, null, cancellationToken))
        {
            return EmailConflict(email);
        }

        var user = new AppUser
        {
            UnitId = caller.UnitId,
            FullName = AccountRules.CleanName(request.FullName),
            Email = email,
            Role = request.Role!,
            ActiveStatus = request.ActiveStatus ?? "active",
            PasswordHash = AccountRules.HashPassword(request.Password!),
            MustChangePassword = true,
        };
        db.AppUsers.Add(user);
        audit.Add(
            caller,
            "user",
            "added",
            $"Added {user.FullName} as {RoleNames[user.Role]}",
            [new AuditChange("Email", null, email), new AuditChange("Status", null, StatusText(user.ActiveStatus))]
        );
        return await SaveAsync(email, cancellationToken) ?? Ok(UserView(user, deletable: true));
    }

    [HttpPut("{id}")]
    [Authorize(Roles = UserManagers)]
    public async Task<IActionResult> Update(string id, UserRequest request, CancellationToken cancellationToken)
    {
        if (await scope.CallerAsync(User, cancellationToken) is not { } caller)
        {
            return SessionEnded();
        }
        if (Problem(request, passwordRequired: false) is { } problem)
        {
            return BadRequest(new { message = problem });
        }

        var businessId = caller.Unit!.BusinessId;
        var users = await BusinessUsers(businessId).ToListAsync(cancellationToken);
        if (users.FirstOrDefault(u => u.UserId == id) is not { } user)
        {
            return NotFound(new { message = "This user no longer exists." });
        }
        if (user.IsAdmin && !caller.IsAdmin)
        {
            return AdminOnly();
        }

        var activeStatus = request.ActiveStatus ?? user.ActiveStatus;
        if (user.UserId == caller.UserId && (request.Role != user.Role || activeStatus != "active"))
        {
            return BadRequest(new { message = "You can't change your own role or switch yourself off. Ask another Approver or Decision Maker." });
        }
        if (user.UserId == caller.UserId && !string.IsNullOrEmpty(request.Password))
        {
            return BadRequest(new { message = "Change your own password with the Password button at the top of the page." });
        }

        var email = request.Email!.Trim().ToLowerInvariant();
        if (await EmailTakenAsync(email, id, cancellationToken))
        {
            return EmailConflict(email);
        }

        var keyRolesBefore = ActiveKeyRoles(users);
        var newName = AccountRules.CleanName(request.FullName);
        var changes = AuditLog
            .Changed(
                new AuditChange("Name", user.FullName, newName),
                new AuditChange("Email", user.Email, email),
                new AuditChange("Role", RoleNames[user.Role], RoleNames[request.Role!]),
                new AuditChange("Status", StatusText(user.ActiveStatus), StatusText(activeStatus)),
                new AuditChange("Password", null, string.IsNullOrEmpty(request.Password) ? null : "Temporary password set")
            )
            .ToList();
        if (changes.Count > 0)
        {
            var (action, summary) = changes is [{ Field: "Status" }]
                ? activeStatus == "active"
                    ? ("switched_on", $"Switched {newName} back on")
                    : ("switched_off", $"Switched off {newName}")
                : ("changed", $"Changed {newName}'s details");
            audit.Add(caller, "user", action, summary, changes);
        }
        user.FullName = AccountRules.CleanName(request.FullName);
        user.Email = email;
        user.Role = request.Role!;
        user.ActiveStatus = activeStatus;
        if (!string.IsNullOrEmpty(request.Password))
        {
            user.PasswordHash = AccountRules.HashPassword(request.Password);
            user.MustChangePassword = true;
        }
        if (LostKeyRole(keyRolesBefore, users) is { } missing)
        {
            return KeyRoleConflict(missing);
        }

        var onRecord = await scope.UsersOnRecordAsync(businessId, cancellationToken);
        return await SaveAsync(email, cancellationToken) ?? Ok(UserView(user, !onRecord.Contains(user.UserId)));
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = UserManagers)]
    public async Task<IActionResult> Delete(
        string id,
        [FromBody] DeleteUserRequest? request,
        CancellationToken cancellationToken
    )
    {
        if (await scope.CallerAsync(User, cancellationToken) is not { } caller)
        {
            return SessionEnded();
        }

        var businessId = caller.Unit!.BusinessId;
        var users = await BusinessUsers(businessId).ToListAsync(cancellationToken);
        if (users.FirstOrDefault(u => u.UserId == id) is not { } user)
        {
            return NotFound(new { message = "This user no longer exists." });
        }
        if (user.UserId == caller.UserId)
        {
            return BadRequest(new { message = "You can't delete your own account." });
        }
        if (user.IsAdmin && !caller.IsAdmin)
        {
            return AdminOnly();
        }

        var onRecord = (await scope.UsersOnRecordAsync(businessId, cancellationToken)).Contains(user.UserId);
        if (caller.IsAdmin)
        {
            if (string.IsNullOrEmpty(request?.Password))
            {
                return BadRequest(new { message = "Type your password to confirm." });
            }
            if (caller.PasswordHash is not { } hash || !BCrypt.Net.BCrypt.Verify(request.Password, hash))
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new { message = "Incorrect password, so the user was not deleted." }
                );
            }
        }
        else if (onRecord)
        {
            return OnRecordConflict();
        }
        if (LostKeyRole(ActiveKeyRoles(users), users.Where(u => u != user)) is { } missing)
        {
            return KeyRoleConflict(missing);
        }

        audit.Add(
            caller,
            "user",
            "deleted",
            onRecord ? $"Deleted {user.FullName} (their history is kept)" : $"Deleted {user.FullName}",
            [new AuditChange("Email", user.Email, null), new AuditChange("Role", RoleNames[user.Role], null)]
        );
        if (onRecord)
        {
            user.DeletedAt = DateTime.UtcNow;
            user.ActiveStatus = "inactive";
            user.PasswordHash = null;
            user.IsAdmin = false;
        }
        else
        {
            db.AppUsers.Remove(user);
        }
        return await SaveAsync(user.Email, cancellationToken) ?? NoContent();
    }

    private IQueryable<AppUser> BusinessUsers(string businessId) =>
        db.AppUsers.Where(u => u.Unit!.BusinessId == businessId && u.DeletedAt == null);

    private Task<bool> EmailTakenAsync(string email, string? ownId, CancellationToken cancellationToken) =>
        db.AppUsers.AnyAsync(
            u => u.Email.ToLower() == email && u.UserId != ownId && u.DeletedAt == null,
            cancellationToken
        );

    private async Task<IActionResult?> SaveAsync(string email, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return EmailConflict(email);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            return OnRecordConflict();
        }
    }

    private static string? Problem(UserRequest request, bool passwordRequired) =>
        AccountRules.PersonProblem(AccountRules.CleanName(request.FullName), request.Email, request.Password, passwordRequired)
        ?? (request switch
        {
            { Role: not ("submitter" or "approver" or "decision_maker") } => "Choose a role: Submitter, Approver or Decision Maker.",
            { ActiveStatus: not (null or "active" or "inactive") } => "The status must be active or inactive.",
            _ => null,
        });

    private static HashSet<string> ActiveKeyRoles(IEnumerable<AppUser> users) =>
        users.Where(u => u.ActiveStatus == "active" && u.Role is "approver" or "decision_maker").Select(u => u.Role).ToHashSet();

    private static string? LostKeyRole(HashSet<string> before, IEnumerable<AppUser> after) =>
        before.Except(ActiveKeyRoles(after)).FirstOrDefault();

    private static string StatusText(string activeStatus) => activeStatus == "active" ? "Active" : "Switched off";

    private ObjectResult AdminOnly() =>
        StatusCode(StatusCodes.Status403Forbidden, new { message = "Only an admin can change or delete an admin account." });

    private UnauthorizedObjectResult SessionEnded() => Unauthorized(new { message = "Your session has ended. Sign in again." });

    private ConflictObjectResult EmailConflict(string email) =>
        Conflict(new { message = $"{email} already has an account. Use another email address." });

    private ConflictObjectResult KeyRoleConflict(string role) =>
        Conflict(new { message = $"This would leave the business without an active {RoleNames[role]}, so the change can't be saved." });

    private ConflictObjectResult OnRecordConflict() =>
        Conflict(new { message = "This user has uploads or decisions on record, so they can only be switched off." });

    private static object UserView(AppUser user, bool deletable) =>
        new
        {
            id = user.UserId,
            user.FullName,
            user.Email,
            user.Role,
            active = user.ActiveStatus == "active",
            user.IsAdmin,
            user.MustChangePassword,
            deletable,
        };
}
