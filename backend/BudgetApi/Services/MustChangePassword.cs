namespace BudgetApi.Services;

[AttributeUsage(AttributeTargets.Method)]
public sealed class AllowTemporaryPasswordAttribute : Attribute;

public static class MustChangePassword
{
    public static async Task Enforce(HttpContext context, Func<Task> next)
    {
        if (
            context.FindCaller() is { MustChangePassword: true }
            && context.GetEndpoint()?.Metadata.GetMetadata<AllowTemporaryPasswordAttribute>() is null
        )
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(
                new { message = "Choose your own password before you carry on." },
                context.RequestAborted
            );
            return;
        }
        await next();
    }
}
