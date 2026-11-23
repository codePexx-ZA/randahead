using System.Text.Json.Serialization;
using BudgetApi.Data;
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
public class SupportController(BudgetDbContext db, Mailer mailer, AuditLog audit) : ApiController
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
        var caller = Caller;

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
        var body = $"""
            From: {name} <{email}>
            Business: {business.LegalName}
            Account: {caller.FullName} ({caller.Username})
            Topic: {topic}

            {message}
            """;
        audit.Add(caller, "support", "sent", $"Support message: {topic}", [
            new AuditChange("From", null, $"{name} <{email}>"),
            new AuditChange("Message", null, message),
        ]);
        await db.SaveChangesAsync(cancellationToken);
        var sent = await mailer.SendAsync(mailer.SupportTo, $"Support: {topic} ({business.LegalName})", body, cancellationToken, email);
        return Ok(new
        {
            message = sent
                ? $"Message sent to {mailer.SupportTo}. We'll get back to you within one business day."
                : "Message saved in the activity log. Email delivery is not set up on this server yet.",
        });
    }
}
