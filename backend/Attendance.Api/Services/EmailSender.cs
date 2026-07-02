using System.Net;
using System.Text;
using System.Text.Json;
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

    // Shared client for the Brevo HTTP API (works over HTTPS 443 — Render never
    // blocks it, unlike outbound SMTP which the free tier drops).
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>Configured when either Brevo (HTTP API) or SMTP creds are present.
    /// Brevo takes priority because SMTP is blocked on Render's free tier.</summary>
    public bool Enabled =>
        !string.IsNullOrWhiteSpace(Env("BREVO_API_KEY")) ||
        (!string.IsNullOrWhiteSpace(Env("SMTP_USER")) && !string.IsNullOrWhiteSpace(Env("SMTP_PASS")));

    /// <summary>Send a plain-HTML email. Never throws — failures are logged and swallowed
    /// so email problems can never break the request that triggered them.</summary>
    public async Task<bool> SendAsync(string toEmail, string subject, string htmlBody)
        => (await SendReturningErrorAsync(toEmail, subject, htmlBody)) is null;

    /// <summary>Like <see cref="SendAsync"/> but returns null on success or a short error
    /// message on failure — used by the /email-test diagnostic so the admin can see the
    /// real SMTP problem without digging through server logs.</summary>
    public async Task<string?> SendReturningErrorAsync(string toEmail, string subject, string htmlBody)
    {
        if (!Enabled) return "Email not configured (set BREVO_API_KEY, or SMTP_USER/SMTP_PASS).";
        if (string.IsNullOrWhiteSpace(toEmail)) return "No recipient email.";

        // Prefer the Brevo HTTP API — it works on Render (HTTPS), SMTP does not.
        var brevoKey = Env("BREVO_API_KEY");
        if (!string.IsNullOrWhiteSpace(brevoKey))
            return await SendViaBrevoAsync(brevoKey!, toEmail, subject, htmlBody);

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
            return null;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Email send failed to {To} via {Host}:{Port}: {Subject}",
                toEmail, host, port, subject);
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    /// <summary>Send via the Brevo transactional-email HTTP API (HTTPS). The sender
    /// address (SMTP_FROM ?? SMTP_USER) must be a VERIFIED sender in the Brevo account.</summary>
    private async Task<string?> SendViaBrevoAsync(string apiKey, string toEmail, string subject, string htmlBody)
    {
        var fromEmail = (Env("SMTP_FROM") ?? Env("SMTP_USER") ?? "").Trim();
        var fromName = Env("SMTP_FROM_NAME") ?? "Attendance";
        if (string.IsNullOrWhiteSpace(fromEmail))
            return "No sender address (set SMTP_FROM or SMTP_USER to your verified Brevo sender).";
        try
        {
            var payload = new
            {
                sender = new { name = fromName, email = fromEmail },
                to = new[] { new { email = toEmail } },
                subject,
                htmlContent = htmlBody,
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
            req.Headers.Add("api-key", apiKey);
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var resp = await _http.SendAsync(req);
            if (resp.IsSuccessStatusCode)
            {
                _log.LogInformation("Email sent to {To} via Brevo: {Subject}", toEmail, subject);
                return null;
            }
            var body = await resp.Content.ReadAsStringAsync();
            _log.LogWarning("Brevo send failed ({Status}) to {To}: {Body}", (int)resp.StatusCode, toEmail, body);
            return $"Brevo {(int)resp.StatusCode}: {body}";
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Brevo send failed to {To}: {Subject}", toEmail, subject);
            return $"{ex.GetType().Name}: {ex.Message}";
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
