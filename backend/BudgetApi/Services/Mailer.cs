// Mailer: sends plain-text emails over SMTP (Mailpit in development), logs the email when sending fails and reports whether it went out
using System.Net;
using System.Net.Mail;

namespace BudgetApi.Services;

public class MailSettings
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 1025;
    public bool EnableSsl { get; set; }
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "RandAhead <no-reply@randahead.local>";
    public string WebAppUrl { get; set; } = "http://127.0.0.1:5500";
    public string SupportTo { get; set; } = "support@randahead.local";
}

public class Mailer(MailSettings settings, ILogger<Mailer> logger)
{
    public string WebAppUrl => settings.WebAppUrl.TrimEnd('/');

    public string SupportTo => settings.SupportTo;

    public async Task<bool> SendAsync(
        string to,
        string subject,
        string body,
        CancellationToken cancellationToken,
        string? replyTo = null
    )
    {
        if (string.IsNullOrWhiteSpace(settings.Host))
        {
            logger.LogWarning("Mail:Host is not set, so this email was not sent. To {To}: {Subject}\n{Body}", to, subject, body);
            return false;
        }
        try
        {
            using var client = new SmtpClient(settings.Host, settings.Port) { EnableSsl = settings.EnableSsl };
            if (settings.Username != "")
            {
                client.Credentials = new NetworkCredential(settings.Username, settings.Password);
            }
            using var message = new MailMessage { From = new MailAddress(settings.From), Subject = subject, Body = body };
            message.To.Add(to);
            if (replyTo is not null)
            {
                message.ReplyToList.Add(replyTo);
            }
            await client.SendMailAsync(message, cancellationToken);
            return true;
        }
        catch (Exception error) when (error is SmtpException or InvalidOperationException or IOException or FormatException)
        {
            logger.LogWarning(error, "Could not send an email. To {To}: {Subject}\n{Body}", to, subject, body);
            return false;
        }
    }
}
