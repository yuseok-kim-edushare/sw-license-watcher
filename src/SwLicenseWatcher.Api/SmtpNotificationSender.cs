using System.Net;
using System.Net.Mail;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

public sealed class SmtpNotificationSender(
    IOptions<NotificationOptions> options,
    ILogger<SmtpNotificationSender> logger) : INotificationSender
{
    private static readonly object CallbackLock = new();
    private static bool s_callbackRegistered;

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

            EnsureCertificateValidationCallback();

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

    private void EnsureCertificateValidationCallback()
    {
        var smtp = options.Value.Smtp;
        if (!smtp.EnableSsl || !SmtpCertificatePinning.HasAllowedThumbprints(smtp.AllowedCertificateThumbprints))
        {
            return;
        }

        if (s_callbackRegistered)
        {
            return;
        }

        lock (CallbackLock)
        {
            if (s_callbackRegistered)
            {
                return;
            }

#pragma warning disable SYSLIB0014 // ServicePointManager is obsolete; SmtpClient still uses this callback for TLS.
            var previous = ServicePointManager.ServerCertificateValidationCallback;
            ServicePointManager.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) =>
                ValidateCertificate(sender, certificate, chain, sslPolicyErrors, previous);
#pragma warning restore SYSLIB0014
            s_callbackRegistered = true;
        }
    }

    private bool ValidateCertificate(
        object sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors,
        RemoteCertificateValidationCallback? previous)
    {
        var allowed = SmtpCertificatePinning.CreateAllowedSet(options.Value.Smtp.AllowedCertificateThumbprints);
        var hasThumbprints = SmtpCertificatePinning.TryGetThumbprints(certificate, out var sha1, out var sha256);
        if (SmtpCertificatePinning.Accept(certificate, sslPolicyErrors, allowed))
        {
            if (hasThumbprints && SmtpCertificatePinning.IsPinned(sha1, sha256, allowed))
            {
                logger.LogDebug(
                    "SMTP TLS certificate allowed by thumbprint pin. SHA-1: {Sha1}. SHA-256: {Sha256}.",
                    sha1,
                    sha256);
            }

            return true;
        }

        if (previous is not null && previous(sender, certificate, chain, sslPolicyErrors))
        {
            return true;
        }

        logger.LogWarning(
            "SMTP TLS certificate was rejected. SHA-1: {Sha1}. SHA-256: {Sha256}. Subject: {Subject}. Issuer: {Issuer}. SslPolicyErrors: {SslPolicyErrors}. ChainStatus: {ChainStatus}. Add the SHA-1 or SHA-256 thumbprint to Notifications:Smtp:AllowedCertificateThumbprints to allow a private-CA certificate.",
            string.IsNullOrEmpty(sha1) ? "(none)" : sha1,
            string.IsNullOrEmpty(sha256) ? "(none)" : sha256,
            certificate?.Subject ?? "(none)",
            certificate?.Issuer ?? "(none)",
            sslPolicyErrors,
            FormatChainStatus(chain));
        return false;
    }

    private static string FormatChainStatus(X509Chain? chain)
    {
        if (chain?.ChainStatus is not { Length: > 0 } status)
        {
            return "(none)";
        }

        return string.Join("; ", status.Select(item => $"{item.Status}: {item.StatusInformation.Trim()}"));
    }
}
