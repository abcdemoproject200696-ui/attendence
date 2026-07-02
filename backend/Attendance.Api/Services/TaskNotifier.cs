using Attendance.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Attendance.Api.Services;

/// <summary>
/// Sends the task-assigned email + phone push in the BACKGROUND so task creation
/// never waits on (or fails because of) email/FCM. Fire-and-forget: the HTTP
/// request returns immediately; this runs on its own DI scope + DbContext.
/// </summary>
public class TaskNotifier
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EmailSender _email;
    private readonly PushSender _push;
    private readonly ILogger<TaskNotifier> _log;

    public TaskNotifier(IServiceScopeFactory scopeFactory, EmailSender email, PushSender push, ILogger<TaskNotifier> log)
    {
        _scopeFactory = scopeFactory;
        _email = email;
        _push = push;
        _log = log;
    }

    /// <summary>Queue notification for a just-created task and return immediately.</summary>
    public void FireAndForget(int taskId) => _ = RunAsync(taskId);

    private async Task RunAsync(int taskId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var t = await db.Tasks.AsNoTracking()
                .Include(x => x.Assignee)
                .Include(x => x.AssignedBy)
                .Include(x => x.Project)
                .FirstOrDefaultAsync(x => x.Id == taskId);
            if (t?.Assignee is null || !t.Assignee.IsActive) return; // never notify inactive

            // ----- Email (gated by the admin setting) -----
            if (_email.Enabled && !string.IsNullOrWhiteSpace(t.Assignee.Email))
            {
                var settings = await db.Settings.AsNoTracking().FirstOrDefaultAsync();
                if (settings is not null && settings.TaskAssignEmail)
                    await _email.SendTaskAssignedAsync(
                        t.Assignee.Email!, t.Assignee.Name, t.Title,
                        t.Project?.Name, t.DueDate, t.Priority, t.AssignedBy?.Name);
            }

            // ----- Phone push (screen-off / app-closed) -----
            if (_push.Enabled)
            {
                var tokens = await db.DeviceTokens.Where(d => d.EmployeeId == t.AssigneeId)
                    .Select(d => d.Token).ToListAsync();
                if (tokens.Count > 0)
                {
                    var body = string.IsNullOrWhiteSpace(t.Project?.Name) ? t.Title : $"{t.Title} · {t.Project!.Name}";
                    var dead = await _push.SendToTokensAsync(
                        tokens, "New task assigned", body, new Dictionary<string, string> { ["type"] = "task" });
                    if (dead.Count > 0)
                    {
                        db.DeviceTokens.RemoveRange(db.DeviceTokens.Where(d => dead.Contains(d.Token)));
                        await db.SaveChangesAsync();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Background task notification failed for task {TaskId}.", taskId);
        }
    }
}
