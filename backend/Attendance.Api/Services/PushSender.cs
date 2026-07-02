using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;

namespace Attendance.Api.Services;

/// <summary>
/// Sends Firebase Cloud Messaging (FCM) push notifications so a task-assigned
/// alert appears on the assignee's phone even with the screen off or the app
/// closed. Credentials come from the FIREBASE_CREDENTIALS env var (the full
/// service-account JSON) — nothing is hardcoded. When it's missing, <see
/// cref="Enabled"/> is false and every send is a safe no-op.
/// </summary>
public class PushSender
{
    private readonly ILogger<PushSender> _log;
    private static readonly object _gate = new();
    private static bool _initTried;
    private static FirebaseApp? _app;

    public PushSender(ILogger<PushSender> log) => _log = log;

    private static string? Env(string k) => Environment.GetEnvironmentVariable(k);

    /// <summary>True once a service-account JSON is configured and FirebaseApp initialised.</summary>
    public bool Enabled => EnsureApp() is not null;

    // Initialise the default FirebaseApp exactly once from FIREBASE_CREDENTIALS.
    private FirebaseApp? EnsureApp()
    {
        if (_app is not null) return _app;
        lock (_gate)
        {
            if (_app is not null) return _app;
            if (_initTried) return _app;
            _initTried = true;

            var cred = Env("FIREBASE_CREDENTIALS");
            if (string.IsNullOrWhiteSpace(cred))
            {
                _log.LogInformation("Push disabled (FIREBASE_CREDENTIALS not set).");
                return null;
            }
            try
            {
                // FIREBASE_CREDENTIALS may be the full service-account JSON (Render) OR,
                // for local dev, a PATH to the downloaded service-account .json file.
                var googleCred = File.Exists(cred.Trim())
                    ? GoogleCredential.FromFile(cred.Trim())
                    : GoogleCredential.FromJson(cred);
                _app = FirebaseApp.DefaultInstance ?? FirebaseApp.Create(new AppOptions
                {
                    Credential = googleCred,
                });
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Push disabled (bad FIREBASE_CREDENTIALS).");
                _app = null;
            }
            return _app;
        }
    }

    /// <summary>
    /// Send one notification to a set of device tokens. Returns the tokens that FCM
    /// reported as invalid/unregistered (caller should delete them). Never throws.
    /// </summary>
    public async Task<List<string>> SendToTokensAsync(
        IReadOnlyList<string> tokens, string title, string body, IDictionary<string, string>? data = null)
    {
        var dead = new List<string>();
        var app = EnsureApp();
        if (app is null || tokens.Count == 0) return dead;

        var messaging = FirebaseMessaging.GetMessaging(app);
        foreach (var token in tokens.Distinct())
        {
            var msg = new Message
            {
                Token = token,
                Notification = new Notification { Title = title, Body = body },
                Data = data is null ? null : new Dictionary<string, string>(data),
                Android = new AndroidConfig
                {
                    Priority = Priority.High,
                    Notification = new AndroidNotification { ChannelId = "task_alerts" },
                },
            };
            try
            {
                await messaging.SendAsync(msg);
            }
            catch (FirebaseMessagingException ex)
            {
                // Token no longer valid (app uninstalled / token rotated) → mark for cleanup.
                if (ex.MessagingErrorCode is MessagingErrorCode.Unregistered or MessagingErrorCode.InvalidArgument)
                    dead.Add(token);
                else
                    _log.LogWarning(ex, "Push send failed for a token.");
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Push send failed for a token.");
            }
        }
        return dead;
    }
}
