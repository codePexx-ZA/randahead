// Rate limits: caps sign-in, register and password reset attempts per client IP and answers 429 with a readable message
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace BudgetApi.Services;

public static class RateLimits
{
    public const string Login = "login";
    public const string Register = "register";
    public const string PasswordReset = "password-reset";
    private const int Segments = 5;

    private static readonly Dictionary<string, (int Limit, TimeSpan Window)> Policies = new()
    {
        [Login] = (10, TimeSpan.FromMinutes(5)),
        [Register] = (5, TimeSpan.FromHours(1)),
        [PasswordReset] = (5, TimeSpan.FromMinutes(15)),
    };

    public static IServiceCollection AddAuthRateLimits(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            foreach (var (name, (limit, window)) in Policies)
            {
                options.AddPolicy(name, context => PerClient(context, name, limit, window));
            }
            options.OnRejected = async (rejected, cancellationToken) =>
            {
                var policy = rejected.HttpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
                var wait = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? retryAfter
                    : policy is not null && Policies.TryGetValue(policy, out var settings) ? settings.Window / Segments
                    : TimeSpan.FromMinutes(1);
                var minutes = Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes));
                var response = rejected.HttpContext.Response;
                response.StatusCode = StatusCodes.Status429TooManyRequests;
                response.Headers.RetryAfter = ((int)Math.Ceiling(wait.TotalSeconds)).ToString();
                await response.WriteAsJsonAsync(
                    new { message = $"Too many attempts. Wait {minutes} minute{(minutes == 1 ? "" : "s")} and try again." },
                    cancellationToken
                );
            };
        });

    private static RateLimitPartition<string> PerClient(HttpContext context, string policy, int limit, TimeSpan window) =>
        RateLimitPartition.GetSlidingWindowLimiter(
            $"{policy}:{context.Connection.RemoteIpAddress}",
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = limit,
                Window = window,
                SegmentsPerWindow = Segments,
                QueueLimit = 0,
            }
        );
}
