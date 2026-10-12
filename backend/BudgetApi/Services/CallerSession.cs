// Caller session: loads the signed-in user once per request while checking their token, and hands it to the rest of the request
using BudgetApi.Data;
using BudgetApi.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BudgetApi.Services;

public static class CallerSession
{
    private const string Key = "caller";

    public static IQueryable<AppUser> Live(this IQueryable<AppUser> users) =>
        users.Where(u => u.ActiveStatus == "active" && u.DeletedAt == null);

    // Tracked on the request's own BudgetDbContext, so controllers can edit the caller (and their business) and save it
    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        var db = context.HttpContext.RequestServices.GetRequiredService<BudgetDbContext>();
        var userId = context.Principal?.FindFirst("sub")?.Value;
        var user = await db
            .AppUsers.Live()
            .Include(u => u.Unit!)
            .ThenInclude(u => u.Business)
            .FirstOrDefaultAsync(u => u.UserId == userId, context.HttpContext.RequestAborted);
        // iat is whole seconds, so a token issued in the same second as the change still counts as newer
        if (
            user is null
            || user.Role != context.Principal?.FindFirst("role")?.Value
            || context.SecurityToken is not JsonWebToken { IssuedAt: var issuedAt }
            || issuedAt.AddSeconds(1) <= user.PasswordChangedAt
        )
        {
            context.Fail("The account changed after this token was issued.");
            return;
        }
        context.HttpContext.Items[Key] = user;
    }

    public static AppUser? FindCaller(this HttpContext context) => context.Items[Key] as AppUser;

    public static AppUser Caller(this HttpContext context) =>
        context.FindCaller() ?? throw new InvalidOperationException("This request has no signed-in user.");
}
