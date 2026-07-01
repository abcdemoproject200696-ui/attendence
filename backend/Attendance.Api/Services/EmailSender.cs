using System.Net;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Attendance.Api.Services;

/// <summary>
/// Minimal SMTP email sender (Gmail-friendly). All credentials come from environment
/// variables (set on Render) — nothing is hardcoded:
///   SMTP_USER      the sender Gmail address, e.g. myteam@gmail.com   (required)
///   SMTP_PASS      a Google "App Password" (16 chars, no spaces)     (required)
///   SMTP_HOST      SMTP host              (optional, default smtp.gmail.com)
///   SMTP_PORT      SMTP port              (optional, default 587)
///   SMTP_FROM      From address           (optional, default = SMTP_USER)
///   SMTP_FROM_NAME Display name           (optional, default "Attendance")
/// When SMTP_USER / SMTP_PASS are missing, <see cref="Enabled"/> is false and
/// sends become a safe no-op (so the app runs fine before email is configured).
/// </summary>
public class EmailSender
{
    private readonly ILogger<EmailSender> _log;
    public EmailSender(ILogger<EmailSender> log) => _log = log;

    private static string? Env(string k) => Environment.GetEnvironmentVariable(k);

    // ===== Tech Anusiya brand shell — same green header + footer as the employee
    // ID card, rebuilt in pure HTML (no hosted image) so it renders in every mail
    // client. The gradient falls back to solid green on old clients. =====

    // Green header band: white "TA" badge + "TECH ANUSIYA" title + subtitle.
    private const string HeaderBand =
        "<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" width=\"100%\" " +
        "style=\"background-color:#16A34A;background-image:linear-gradient(90deg,#22C55E,#16A34A)\"><tr>" +
        "<td style=\"padding:14px 18px\">" +
        "<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\"><tr>" +
        "<td style=\"width:40px;height:40px;background:#ffffff;border-radius:9px;text-align:center;" +
        "vertical-align:middle;color:#16A34A;font-size:18px;font-weight:bold;font-family:Segoe UI,Arial,sans-serif\">TA</td>" +
        "<td style=\"padding-left:12px;font-family:Segoe UI,Arial,sans-serif\">" +
        "<div style=\"font-size:19px;font-weight:700;color:#ffffff;letter-spacing:0.5px\">TECH ANUSIYA</div>" +
        "<div style=\"font-size:11px;color:#e6ffe6\">Attendance &amp; Task Management</div>" +
        "</td></tr></table></td></tr></table>";

    // Green footer band: tagline + support email (from SUPPORT_EMAIL / sender).
    private static string FooterBand()
    {
        // Support address shown in the footer; override anytime via the SUPPORT_EMAIL env var.
        var support = Env("SUPPORT_EMAIL") ?? "testemail@techanusiya.com";
        var help = $"<div style=\"font-size:11px;color:#e6ffe6;padding-top:3px\">Need help? {WebUtility.HtmlEncode(support)}</div>";
        return "<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" width=\"100%\" " +
            "style=\"background-color:#16A34A;background-image:linear-gradient(90deg,#22C55E,#16A34A)\"><tr>" +
            "<td style=\"padding:12px 18px;text-align:center;font-family:Segoe UI,Arial,sans-serif\">" +
            "<div style=\"font-size:12px;color:#ffffff;font-weight:600\">Tech Anusiya &nbsp;&#183;&nbsp; Innovating Together</div>" +
            help + "</td></tr></table>";
    }

    /// <summary>Wrap body HTML in the branded card: header band + content + footer band.</summary>
    private static string Shell(string bodyHtml) =>
        "<div style=\"font-family:Segoe UI,Arial,sans-serif;max-width:520px;margin:auto;" +
        "border:1px solid #e3e8ee;border-radius:12px;overflow:hidden;background:#ffffff\">" +
        HeaderBand +
        "<div style=\"padding:22px\">" + bodyHtml + "</div>" +
        FooterBand() + "</div>";

    /// <summary>True only when SMTP_USER and SMTP_PASS are both configured.</summary>
    public bool Enabled =>
        !string.IsNullOrWhiteSpace(Env("SMTP_USER")) &&
        !string.IsNullOrWhiteSpace(Env("SMTP_PASS"));

    /// <summary>Send a plain-HTML email. Never throws — failures are logged and swallowed
    /// so email problems can never break the request that triggered them.</summary>
    public async Task<bool> SendAsync(string toEmail, string subject, string htmlBody)
    {
        if (!Enabled)
        {
            _log.LogInformation("Email skipped (SMTP not configured): {Subject}", subject);
            return false;
        }
        if (string.IsNullOrWhiteSpace(toEmail))
        {
            _log.LogInformation("Email skipped (no recipient): {Subject}", subject);
            return false;
        }

        var user = Env("SMTP_USER")!;
        var pass = Env("SMTP_PASS")!;
        var host = (Env("SMTP_HOST") ?? "smtp.gmail.com").Trim();
        var port = int.TryParse(Env("SMTP_PORT"), out var p) ? p : 587;
        var from = (Env("SMTP_FROM") ?? user).Trim();
        var fromName = Env("SMTP_FROM_NAME") ?? "Attendance";

        try
        {
            var msg = new MimeMessage();
            msg.From.Add(new MailboxAddress(fromName, from));
            msg.To.Add(MailboxAddress.Parse(toEmail));
            msg.Subject = subject;
            msg.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

            // Pick the TLS mode by port: 465 = implicit SSL, otherwise STARTTLS.
            // MailKit handles both correctly (System.Net.Mail could not do 465).
            var security = port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;

            using var client = new SmtpClient { Timeout = 20000 }; // 20s — fail fast, no long hang
            await client.ConnectAsync(host, port, security);
            await client.AuthenticateAsync(user, pass);
            await client.SendAsync(msg);
            await client.DisconnectAsync(true);
            _log.LogInformation("Email sent to {To}: {Subject}", toEmail, subject);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Email send failed to {To} via {Host}:{Port}: {Subject}",
                toEmail, host, port, subject);
            return false;
        }
    }

    /// <summary>Build + send the "a task was assigned to you" email.</summary>
    public Task<bool> SendTaskAssignedAsync(
        string toEmail, string toName, string taskTitle,
        string? project, string? dueDate, string? priority, string? assignedBy)
    {
        var rows = "";
        void Row(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                rows += $"<tr><td style=\"padding:4px 12px 4px 0;color:#666\">{label}</td>" +
                        $"<td style=\"padding:4px 0;font-weight:600\">{WebUtility.HtmlEncode(value)}</td></tr>";
        }
        Row("Project", project);
        Row("Due date", dueDate);
        Row("Priority", priority);
        Row("Assigned by", assignedBy);

        var safeName = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(toName) ? "there" : toName);
        var safeTitle = WebUtility.HtmlEncode(taskTitle);
        var body =
            $"<h2 style=\"color:#16A34A;margin:0 0 4px\">New task assigned to you</h2>" +
            $"<p>Hi {safeName},</p>" +
            $"<p>A new task has been assigned to you:</p>" +
            $"<div style=\"background:#f5f7fa;border-radius:8px;padding:14px 16px;margin:10px 0\">" +
            $"<div style=\"font-size:16px;font-weight:700;margin-bottom:8px\">{safeTitle}</div>" +
            $"<table style=\"font-size:14px\">{rows}</table></div>" +
            $"<p style=\"color:#888;font-size:12px;margin-bottom:0\">This is an automated message — please do not reply.</p>";

        return SendAsync(toEmail, $"New task assigned: {taskTitle}", Shell(body));
    }

    /// <summary>Build + send the signup verification (OTP) email.</summary>
    public Task<bool> SendSignupOtpAsync(string toEmail, string code)
    {
        var body =
            $"<h2 style=\"color:#16A34A;margin:0 0 4px\">Verify your email</h2>" +
            $"<p>Use this code to complete your sign up:</p>" +
            $"<div style=\"background:#f5f7fa;border-radius:8px;padding:18px;margin:12px 0;text-align:center\">" +
            $"<span style=\"font-size:30px;font-weight:800;letter-spacing:8px;color:#16A34A\">{WebUtility.HtmlEncode(code)}</span>" +
            $"</div>" +
            $"<p style=\"color:#666;font-size:13px\">This code is valid for 10 minutes. If you didn't request it, ignore this email.</p>" +
            $"<p style=\"color:#888;font-size:12px;margin-bottom:0\">Automated message — please do not reply.</p>";
        return SendAsync(toEmail, $"Your verification code: {code}", Shell(body));
    }
}
