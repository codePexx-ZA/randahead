using BudgetApi.Data;
using BudgetApi.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

namespace BudgetApi.Services;

public static class CallerSession
{
    private const string Key = "caller";

    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        var db = context.HttpContext.RequestServices.GetRequiredService<BudgetDbContext>();
        var userId = context.Principal?.FindFirst("sub")?.Value;
        var user = await db
            .AppUsers.Include(u => u.Unit!)
            .ThenInclude(u => u.Business)
            .FirstOrDefaultAsync(u => u.UserId == userId, context.HttpContext.RequestAborted);
        if (user is null)
        {
            context.Fail("The account behind this token no longer exists.");
            return;
        }
        context.HttpContext.Items[Key] = user;
    }

    public static AppUser? FindCaller(this HttpContext context) => context.Items[Key] as AppUser;

    public static AppUser Caller(this HttpContext context) =>
        context.FindCaller() ?? throw new InvalidOperationException("This request has no signed-in user.");
}
