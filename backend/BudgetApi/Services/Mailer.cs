// Mailer: sends plain-text emails over SMTP (Mailpit in development) and logs the email when sending fails
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
}

public class Mailer(MailSettings settings, ILogger<Mailer> logger)
{
    public string WebAppUrl => settings.WebAppUrl.TrimEnd('/');

    public async Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Host))
        {
            logger.LogWarning("Mail:Host is not set, so this email was not sent. To {To}: {Subject}\n{Body}", to, subject, body);
            return;
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
            await client.SendMailAsync(message, cancellationToken);
        }
        catch (Exception error) when (error is SmtpException or InvalidOperationException or IOException)
        {
            logger.LogWarning(error, "Could not send an email. To {To}: {Subject}\n{Body}", to, subject, body);
        }
    }
}
