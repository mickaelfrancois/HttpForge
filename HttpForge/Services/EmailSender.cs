using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace HttpForge.Services;

public class EmailSender(IOptions<SmtpSettings> options, ILogger<EmailSender> logger)
{
    private readonly SmtpSettings _settings = options.Value;

    public bool IsConfigured => _settings.IsConfigured;

    public async Task SendInvitationAsync(string toEmail, string inviteUrl, string role, CancellationToken ct = default)
    {
        if (!IsConfigured) return;

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_settings.From));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = "You've been invited to HttpForge";

        var roleLabel = role switch
        {
            "SuperAdmin"  => "Super Admin",
            "TeamAdmin"   => "Team Admin",
            "Contributor" => "Contributor",
            "Guest"       => "Guest (read-only)",
            _             => role
        };

        message.Body = new TextPart("html")
        {
            Text = $"""
                <p>You've been invited to join <strong>HttpForge</strong> as <strong>{roleLabel}</strong>.</p>
                <p>Click the link below to create your account:</p>
                <p><a href="{inviteUrl}">{inviteUrl}</a></p>
                <p>This link expires in 72 hours.</p>
                <hr/>
                <p style="font-size:0.85em;color:#666">If you did not expect this invitation, you can ignore this email.</p>
                """
        };

        try
        {
            using var client = new SmtpClient();
            var secureSocket = _settings.EnableSsl
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.None;

            await client.ConnectAsync(_settings.Host, _settings.Port, secureSocket, ct);

            if (!string.IsNullOrEmpty(_settings.User) && _settings.Password is not null)
                await client.AuthenticateAsync(_settings.User, _settings.Password, ct);

            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);

            logger.LogInformation("Invitation email sent to {Email}", toEmail);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send invitation email to {Email}", toEmail);
            throw;
        }
    }
}
