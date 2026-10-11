// Support API: emails a signed-in user's support message to the support inbox, with their email as the reply address
using System.Text.Json.Serialization;
using BudgetApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BudgetApi.Controllers;

public record SupportRequest(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("topic")] string? Topic,
    [property: JsonPropertyName("message")] string? Message
);

[ApiController]
[Route("api/support")]
[Authorize]
public class SupportController(BusinessScope scope, Mailer mailer) : ControllerBase
{
    private const int MaxMessageLength = 2000;
    private static readonly string[] Topics =
    [
        "Uploading a statement",
        "Forecasts and budgets",
        "Account and access",
        "Something else",
    ];

    [HttpPost]
    [EnableRateLimiting(RateLimits.Support)]
    public async Task<IActionResult> Send(SupportRequest request, CancellationToken cancellationToken)
    {
        if (await scope.CallerAsync(User, cancellationToken) is not { } caller)
        {
            return Unauthorized(new { message = "Your session has ended. Sign in again." });
        }

        var name = AccountRules.CleanName(request.Name);
        var email = request.Email?.Trim() ?? "";
        var topic = request.Topic?.Trim() ?? "";
        var message = request.Message?.Trim() ?? "";
        var problem = true switch
        {
            _ when name.Length == 0 => "Type your name.",
            _ when name.Length > 200 => "Your name can be at most 200 characters.",
            _ when !AccountRules.IsValidEmail(email) => "Type a valid email address, e.g. name@business.co.za.",
            _ when !Topics.Contains(topic) => "Choose one of the listed topics.",
            _ when message.Length == 0 => "Type your message.",
            _ when message.Length > MaxMessageLength => $"The message can be at most {MaxMessageLength} characters.",
            _ => null,
        };
        if (problem is not null)
        {
            return BadRequest(new { message = problem });
        }

        var business = caller.Unit!.Business!;
        var role = AuditLog.RoleNames.GetValueOrDefault(caller.Role, caller.Role) + (caller.IsAdmin ? " (admin)" : "");
        var body = $"""
            From: {name} <{email}>
            Business: {business.LegalName}
            Account: {caller.FullName} <{caller.Email}>, {role}
            Topic: {topic}

            {message}
            """;
        var sent = await mailer.SendAsync(mailer.SupportTo, $"Support: {topic} ({business.LegalName})", body, cancellationToken, email);
        if (!sent)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new { message = "Your message couldn't be sent right now. Try again in a few minutes." }
            );
        }
        return Ok(new { message = "Message sent. We'll get back to you within one business day." });
    }
}
