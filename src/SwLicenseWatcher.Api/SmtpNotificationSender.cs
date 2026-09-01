using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

public sealed class SmtpNotificationSender(
    IOptions<NotificationOptions> options,
    ILogger<SmtpNotificationSender> logger) : INotificationSender
{
    public async Task SendAsync(NotificationMessage message, CancellationToken cancellationToken)
    {
        var smtp = options.Value.Smtp;
        if (!smtp.Enabled)
        {
            return;
        }

        try
        {
            using var mail = new MailMessage
            {
                From = new MailAddress(smtp.From),
                Subject = message.Subject,
                Body = message.Body,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };
            foreach (var recipient in smtp.Recipients)
            {
                if (!string.IsNullOrWhiteSpace(recipient))
                {
                    mail.To.Add(recipient.Trim());
                }
            }

            if (mail.To.Count == 0)
            {
                logger.LogWarning("SMTP notification '{Subject}' skipped because no recipients are configured.", message.Subject);
                return;
            }

#pragma warning disable SYSLIB0014 // SmtpClient is obsolete; MailKit is not Native AOT compatible without MailKitLite.
            using var client = new SmtpClient(smtp.Host, smtp.Port)
            {
                EnableSsl = smtp.EnableSsl
            };
            if (!string.IsNullOrWhiteSpace(smtp.UserName))
            {
                client.Credentials = new NetworkCredential(smtp.UserName, smtp.Password);
            }

            await client.SendMailAsync(mail, cancellationToken);
#pragma warning restore SYSLIB0014
            logger.LogInformation("Sent SMTP notification '{Subject}'.", message.Subject);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SMTP notification '{Subject}' failed.", message.Subject);
        }
    }
}
