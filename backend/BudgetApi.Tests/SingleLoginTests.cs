using System.Reflection;
using System.Text.RegularExpressions;
using BudgetApi.Controllers;
using BudgetApi.Data;
using BudgetApi.Models;
using BudgetApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace BudgetApi.Tests;

public partial class SingleLoginTests
{
    [GeneratedRegex(@"INSERT INTO app_user \(user_id, unit_id, full_name, username, password_hash\) VALUES\s*\('([0-9a-f]{32})', '[0-9a-f]{32}', '[^']+', '([^']+)',\s*'([^']+)'\);")]
    private static partial Regex SeedUser();

    private static string SeedSql() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "schema", "03_seed.sql"));

    [Fact]
    public void Seed_HasExactlyOneLogin_Demo123WithAHashedPassword()
    {
        var sql = SeedSql();
        Assert.Single(Regex.Matches(sql, "INSERT INTO app_user"));
        var match = SeedUser().Match(sql);
        Assert.True(match.Success);
        Assert.Equal("demo123", match.Groups[2].Value);
        var hash = match.Groups[3].Value;
        Assert.StartsWith("$2", hash);
        Assert.True(BCrypt.Net.BCrypt.Verify("test", hash));
        Assert.False(BCrypt.Net.BCrypt.Verify("demo123", hash));
    }

    [Theory]
    [InlineData("demo123")]
    [InlineData(" DEMO123 ")]
    public async Task Login_Succeeds_ForDemo123AndTest(string username)
    {
        await using var db = await SeededDb();
        var result = await NewController(db).Login(new LoginRequest(username, "test"), CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        var token = ok.Value!.GetType().GetProperty("token")!.GetValue(ok.Value) as string;
        Assert.False(string.IsNullOrEmpty(token));
    }

    [Theory]
    [InlineData("demo123", "wrong")]
    [InlineData("demo123", "")]
    [InlineData("owner@demo.co.za", "demo123")]
    [InlineData("admin@gmail.com", "admin")]
    [InlineData("test@gmail.com", "test")]
    public async Task Login_IsRefused_ForAWrongPasswordOrAnOldAccount(string username, string password)
    {
        await using var db = await SeededDb();
        var result = await NewController(db).Login(new LoginRequest(username, password), CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public void EveryEndpoint_NeedsAToken_ExceptLogin()
    {
        var anonymous = new List<string>();
        foreach (var controller in Controllers())
        {
            var classNeedsToken = controller.GetCustomAttribute<AuthorizeAttribute>() is not null;
            foreach (var action in Actions(controller))
            {
                var allowsAnonymous = action.GetCustomAttribute<AllowAnonymousAttribute>() is not null
                    || controller.GetCustomAttribute<AllowAnonymousAttribute>() is not null;
                var needsToken = classNeedsToken || action.GetCustomAttribute<AuthorizeAttribute>() is not null;
                if (allowsAnonymous || !needsToken)
                {
                    anonymous.Add($"{controller.Name}.{action.Name}");
                }
            }
        }
        Assert.Equal(["AuthController.Login"], anonymous);
    }

    [Fact]
    public void NoEndpoint_IsLimitedByRoleOrPolicy()
    {
        var limited = new List<string>();
        foreach (var controller in Controllers())
        {
            var targets = Actions(controller).Cast<MemberInfo>().Prepend(controller);
            foreach (var target in targets)
            {
                var attributes = target.GetCustomAttributes(true);
                if (attributes.OfType<AuthorizeAttribute>().Any(a => a.Roles is not null || a.Policy is not null)
                    || attributes.Any(a => a is IAuthorizationFilter or IAsyncAuthorizationFilter))
                {
                    limited.Add($"{controller.Name}.{target.Name}");
                }
            }
        }
        Assert.Empty(limited);
    }

    private static IEnumerable<Type> Controllers() =>
        typeof(ApiController).Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t));

    private static IEnumerable<MethodInfo> Actions(Type controller) =>
        controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().Any());

    private static AuthController NewController(BudgetDbContext db) =>
        new(db, new TokenService(new JwtSettings { Key = new string('k', 48) }));

    private static async Task<BudgetDbContext> SeededDb()
    {
        var options = new DbContextOptionsBuilder<BudgetDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new BudgetDbContext(options);
        var match = SeedUser().Match(SeedSql());
        db.AppUsers.Add(new AppUser
        {
            UserId = match.Groups[1].Value,
            UnitId = "unit",
            FullName = "Demo User",
            Username = match.Groups[2].Value,
            PasswordHash = match.Groups[3].Value,
        });
        await db.SaveChangesAsync();
        return db;
    }
}
