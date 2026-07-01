using System.Text;
using System.Text.Json;
using Attendance.Domain.Entities;
using Attendance.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Attendance.Api.Controllers;

public class AiCommandDto
{
    public string Text { get; set; } = string.Empty;
    public string Provider { get; set; } = "gemini"; // "gemini" | "groq"
    public int ByEmpId { get; set; }
    // Client's current local date-time "yyyy-MM-ddTHH:mm" — lets the AI turn a
    // duration ("2 hour task") into concrete start/end times off the user's clock.
    public string? NowIso { get; set; }
    // Recent conversation (oldest first) so the AI can answer follow-ups like a chat.
    public List<AiMsg>? History { get; set; }
}

public class AiMsg
{
    public string Role { get; set; } = "user"; // "user" | "ai"
    public string Text { get; set; } = string.Empty;
}

public record AiResult(bool Ok, string Reply, string Provider, bool Changed, string? Nav = null);

// Natural-language task assistant. Takes a user's request, asks an LLM (Gemini or
// Groq, with automatic fallback) to turn it into a structured action, then runs
// that action against the DB. Free tiers: Gemini + Groq. Keys come from env vars
// GEMINI_API_KEY / GROQ_API_KEY (never hardcoded).
[ApiController]
[Route("api/ai")]
public class AiController : ControllerBase
{
    private readonly AppDbContext _db;
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(40) };
    private static readonly string[] ValidStatuses = { "ToDo", "InProgress", "Review", "Done" };
    private static readonly string[] ValidPriorities = { "Low", "Medium", "High", "Urgent" };

    public AiController(AppDbContext db) => _db = db;

    // Tells the app which providers actually have a key configured (for the toggle).
    [HttpGet("providers")]
    public ActionResult<object> Providers() => Ok(new
    {
        gemini = !string.IsNullOrWhiteSpace(Env("GEMINI_API_KEY")),
        groq = !string.IsNullOrWhiteSpace(Env("GROQ_API_KEY")),
    });


    [HttpPost("command")]
    public async Task<ActionResult<AiResult>> Command(AiCommandDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Text))
            return Ok(new AiResult(false, "Please type or say a command.", "", false));

        var employees = await _db.Employees.AsNoTracking().Include(e => e.Role).ToListAsync();
        var projects = await _db.Projects.AsNoTracking().ToListAsync();
        var prompt = BuildPrompt(dto.Text, employees, projects, dto.NowIso, dto.History);

        // Chosen provider first, then fall back to the other (covers quota/limit).
        var order = string.Equals(dto.Provider, "groq", StringComparison.OrdinalIgnoreCase)
            ? new[] { "groq", "gemini" }
            : new[] { "gemini", "groq" };

        string? raw = null;
        var used = "";
        foreach (var p in order)
        {
            try
            {
                raw = p == "gemini" ? await CallGemini(prompt) : await CallGroq(prompt);
                if (!string.IsNullOrWhiteSpace(raw)) { used = p; break; }
            }
            catch { /* try the other provider */ }
        }

        if (string.IsNullOrWhiteSpace(raw))
            return Ok(new AiResult(false,
                "AI is unavailable right now (both providers failed or no API key set on the server).", "", false));

        JsonElement action;
        try { action = JsonDocument.Parse(ExtractJson(raw!)).RootElement; }
        catch { return Ok(new AiResult(true, raw!.Trim(), used, false)); }

        var (reply, changed, nav) = await Execute(action, employees, projects, dto.ByEmpId, dto.NowIso);
        return Ok(new AiResult(true, reply, used, changed, nav));
    }

    // App pages the assistant can open (key -> friendly name). Mirrors the home menu.
    private static readonly Dictionary<string, string> Pages = new()
    {
        ["dashboard"] = "Dashboard", ["kiosk"] = "Attendance Scanner", ["employees"] = "Employees",
        ["idcard"] = "ID Card", ["daily"] = "Daily Attendance", ["report"] = "Monthly Report",
        ["holidays"] = "Holidays", ["leaves"] = "Leaves", ["tasks"] = "Tasks", ["projects"] = "Projects",
        ["salary"] = "Salary", ["settings"] = "Settings", ["permissions"] = "Permissions",
    };

    // ===== Prompt =====
    private static string BuildPrompt(string text, List<Employee> emps, List<Project> projects, string? nowIso, List<AiMsg>? history)
    {
        var now = string.IsNullOrWhiteSpace(nowIso) ? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm") : nowIso!;
        var today = now.Length >= 10 ? now[..10] : now;
        var empList = string.Join(", ", emps.Select(e => $"{e.Name} ({e.Code})"));
        var projList = projects.Count == 0 ? "(none)" : string.Join(", ", projects.Select(p => p.Name));
        var sb = new StringBuilder();
        sb.AppendLine("You are a friendly task-management assistant for a Kanban app. Understand English, Hindi and Hinglish input, but ALWAYS write replies in clear, standard English. Convert the LATEST user message into ONE JSON action. Reply with ONLY a JSON object — no prose, no markdown.");
        sb.AppendLine($"Current date-time is {now}. Today is {today}. Statuses: ToDo, InProgress, Review, Done. Priorities: Low, Medium, High, Urgent.");
        sb.AppendLine($"Employees: {empList}");
        sb.AppendLine($"Projects: {projList}");
        sb.AppendLine("Match nicknames/first names to a real employee name from the list. Dates: yyyy-MM-dd. Date-times: yyyy-MM-ddTHH:mm.");
        sb.AppendLine("IMPORTANT rules (all questions must be in standard English):");
        sb.AppendLine("- CREATING A TASK only requires a title and an assignee. If the assignee is missing, return {\"action\":\"ask\",\"reply\":\"Who should I assign this task to?\"}. Do NOT ask about project, priority, date or time — leave them null unless the user actually gave them; the system fills sensible defaults (project: none, priority: Low, due: today, time: a 30-minute slot from now).");
        sb.AppendLine("- If the user DID give a duration (\"2 hour\", \"30 min\"), set startTime = the current date-time above and endTime = current + that duration.");
        sb.AppendLine("- Use the earlier conversation to fill details the user already gave or answered.");
        sb.AppendLine("Choose ONE action:");
        sb.AppendLine("- Create/assign a task: {\"action\":\"create_task\",\"title\":\"...\",\"assignee\":\"<name>\",\"priority\":null,\"status\":\"ToDo\",\"dueDate\":null,\"startTime\":null,\"endTime\":null,\"project\":null}  (leave priority/dueDate/startTime/endTime/project null unless the user gave them)");
        sb.AppendLine("- Change ONE task (time/date/status/priority/project/assignee/rename): {\"action\":\"change_task\",\"taskTitle\":\"...\",\"dueDate\":null,\"startTime\":null,\"endTime\":null,\"status\":null,\"priority\":null,\"project\":null,\"assignee\":null,\"newTitle\":null}");
        sb.AppendLine("- Bulk move tasks (e.g. \"move all of Amit's ToDo tasks to Done\"): {\"action\":\"bulk_change\",\"assignee\":null,\"project\":null,\"fromStatus\":null,\"toStatus\":\"Done\"}");
        sb.AppendLine("- Show one task's full details: {\"action\":\"task_detail\",\"taskTitle\":\"...\"}");
        sb.AppendLine("- Delete a task: {\"action\":\"delete_task\",\"taskTitle\":\"...\"}");
        sb.AppendLine("- Workload (who has the most open tasks / how busy someone is): {\"action\":\"workload\",\"assignee\":null}");
        sb.AppendLine("- Count tasks (e.g. \"how many Low priority tasks in Project B\"): {\"action\":\"count_tasks\",\"assignee\":null,\"status\":null,\"priority\":null,\"project\":null}");
        sb.AppendLine("- Progress percent: {\"action\":\"progress\",\"assignee\":null,\"project\":null}");
        sb.AppendLine("- List tasks: {\"action\":\"list_tasks\",\"assignee\":null,\"status\":null,\"project\":null,\"priority\":null,\"noProject\":false,\"noAssignee\":false,\"overdue\":false}  — set noProject=true for tasks with no project, noAssignee=true for unassigned tasks, overdue=true for tasks past their due date and not Done.");
        sb.AppendLine("- Who works on a project: {\"action\":\"project_members\",\"project\":null}");
        sb.AppendLine("- Add a comment to a task: {\"action\":\"add_comment\",\"taskTitle\":\"...\",\"comment\":\"...\"}");
        sb.AppendLine("- Open/show an app page: {\"action\":\"navigate\",\"page\":\"<key>\"}  where key is one of: dashboard, kiosk, employees, idcard, daily, report, holidays, leaves, tasks, projects, salary, settings, permissions.");
        sb.AppendLine("- Ask the user for a missing detail: {\"action\":\"ask\",\"reply\":\"<your question>\"}");
        sb.AppendLine("- Anything else / a direct answer: {\"action\":\"chat\",\"reply\":\"...\"}");
        if (history != null && history.Count > 0)
        {
            sb.AppendLine("Conversation so far (oldest first):");
            foreach (var m in history.TakeLast(8))
                sb.AppendLine($"{(m.Role == "ai" ? "Assistant" : "User")}: {m.Text}");
        }
        sb.AppendLine($"Latest user message: {text}");
        return sb.ToString();
    }

    // ===== Provider calls (return the model's text, or null on failure) =====
    // Free-tier model quota varies by project/region (gemini-2.0-flash can be 0),
    // so try several in order and use the first that has quota.
    private static readonly string[] GeminiModels =
        { "gemini-2.5-flash", "gemini-2.0-flash", "gemini-2.5-flash-lite", "gemini-1.5-flash", "gemini-flash-latest" };

    private static async Task<string?> CallGemini(string prompt)
    {
        var key = Env("GEMINI_API_KEY");
        if (string.IsNullOrWhiteSpace(key)) return null;
        var body = JsonSerializer.Serialize(new
        {
            contents = new[] { new { parts = new[] { new { text = prompt } } } },
            generationConfig = new { responseMimeType = "application/json", temperature = 0.2 }
        });
        Exception? last = null;
        foreach (var model in GeminiModels)
        {
            try
            {
                var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={key}";
                var res = await _http.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"));
                if (!res.IsSuccessStatusCode) { last = new Exception($"{model} {(int)res.StatusCode}"); continue; }
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                return doc.RootElement.GetProperty("candidates")[0].GetProperty("content")
                    .GetProperty("parts")[0].GetProperty("text").GetString();
            }
            catch (Exception e) { last = e; }
        }
        if (last != null) throw last;
        return null;
    }

    private static async Task<string?> CallGroq(string prompt)
    {
        var key = Env("GROQ_API_KEY");
        if (string.IsNullOrWhiteSpace(key)) return null;
        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
        req.Headers.Add("Authorization", $"Bearer {key}");
        req.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model = "llama-3.3-70b-versatile",
            messages = new[] { new { role = "user", content = prompt } },
            response_format = new { type = "json_object" },
            temperature = 0.2
        }), Encoding.UTF8, "application/json");
        var res = await _http.SendAsync(req);
        if (!res.IsSuccessStatusCode) throw new Exception($"groq {(int)res.StatusCode}");
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
    }

    // ===== Action execution =====
    private async Task<(string reply, bool changed, string? nav)> Execute(
        JsonElement a, List<Employee> emps, List<Project> projects, int byEmpId, string? nowIso)
    {
        var action = Str(a, "action")?.ToLowerInvariant() ?? "chat";
        switch (action)
        {
            // Creating a task jumps the user to the Tasks board so they see it.
            case "create_task": { var (r, c) = await CreateTask(a, emps, projects, byEmpId, nowIso); return (r, c, c ? "tasks" : null); }
            case "change_task": { var (r, c) = await ChangeTask(a, emps, projects); return (r, c, c ? "tasks" : null); }
            case "bulk_change": { var (r, c) = await BulkChange(a, emps, projects); return (r, c, c ? "tasks" : null); }
            case "task_detail": { var (r, c) = await TaskDetail(a, projects); return (r, c, null); }
            case "delete_task": { var (r, c) = await DeleteTask(a); return (r, c, c ? "tasks" : null); }
            case "workload": { var (r, c) = await Workload(a, emps); return (r, c, null); }
            case "count_tasks": { var (r, c) = await CountTasks(a, emps, projects); return (r, c, null); }
            case "progress": { var (r, c) = await Progress(a, emps, projects); return (r, c, null); }
            case "list_tasks": { var (r, c) = await ListTasks(a, emps, projects, nowIso); return (r, c, null); }
            case "project_members": { var (r, c) = await ProjectMembers(a, projects); return (r, c, null); }
            case "add_comment": { var (r, c) = await AddComment(a, emps, byEmpId); return (r, c, null); }
            case "navigate":
                var key = (Str(a, "page") ?? "").ToLowerInvariant();
                return Pages.ContainsKey(key)
                    ? ($"Opening {Pages[key]}…", false, key)
                    : ("Which page should I open? (e.g. tasks, dashboard, employees)", false, null);
            case "ask":
                return (Str(a, "reply") ?? "Could you give me a bit more detail?", false, null);
            default:
                return (Str(a, "reply") ?? "Sorry, I didn't understand. Try: \"assign a task to <name>\" or \"open tasks page\".", false, null);
        }
    }

    // The current date-time from the client (fallback: server UTC).
    private static DateTime NowOf(string? nowIso) =>
        DateTime.TryParse(nowIso, out var d) ? d : DateTime.UtcNow;

    private async Task<(string, bool)> CreateTask(JsonElement a, List<Employee> emps, List<Project> projects, int byEmpId, string? nowIso)
    {
        var title = Str(a, "title");
        if (string.IsNullOrWhiteSpace(title)) return ("What should the task be called?", false);
        var assigneeName = Str(a, "assignee");
        if (string.IsNullOrWhiteSpace(assigneeName)) return ("Who should I assign this task to?", false);
        var assignee = MatchEmployee(assigneeName, emps);
        if (assignee == null) return ($"I couldn't find an employee named \"{assigneeName}\". Please give the correct name.", false);
        var project = MatchProject(Str(a, "project"), projects); // null = update later
        var status = ValidStatuses.Contains(Str(a, "status")) ? Str(a, "status")! : "ToDo";
        // Defaults when the user didn't say: priority Low, due today, a 30-min slot from now.
        var priority = ValidPriorities.Contains(Str(a, "priority")) ? Str(a, "priority")! : "Low";
        var now = NowOf(nowIso);
        var due = Str(a, "dueDate") ?? now.ToString("yyyy-MM-dd");
        var start = Str(a, "startTime") ?? now.ToString("yyyy-MM-ddTHH:mm");
        var end = Str(a, "endTime")
                  ?? (DateTime.TryParse(start, out var s) ? s.AddMinutes(30) : now.AddMinutes(30)).ToString("yyyy-MM-ddTHH:mm");
        var by = byEmpId > 0 ? byEmpId : assignee.Id;

        var t = new TaskItem
        {
            Title = title!,
            AssigneeId = assignee.Id,
            AssignedById = by,
            Status = status,
            Priority = priority,
            DueDate = due,
            StartTime = start,
            EndTime = end,
            ProjectId = project?.Id,
            CreatedAt = DateTime.UtcNow,
        };
        _db.Tasks.Add(t);
        await _db.SaveChangesAsync();
        var extra = new List<string> { priority, $"due {due}", $"{start[11..]}–{end[11..]}" };
        if (project != null) extra.Add(project.Name);
        return ($"✅ Task \"{title}\" assigned to {assignee.Name} ({string.Join(", ", extra)}).", true);
    }

    private async Task<(string, bool)> ChangeTask(JsonElement a, List<Employee> emps, List<Project> projects)
    {
        var t = await FindTask(Str(a, "taskTitle"));
        if (t is string err) return (err, false);
        var task = (TaskItem)t;

        var changes = new List<string>();
        if (MatchProject(Str(a, "project"), projects) is Project pr) { task.ProjectId = pr.Id; changes.Add($"project {pr.Name}"); }
        if (MatchEmployee(Str(a, "assignee"), emps) is Employee em) { task.AssigneeId = em.Id; changes.Add($"assignee {em.Name}"); }
        if (Str(a, "newTitle") is string nt && nt.Length > 0) { task.Title = nt; changes.Add($"renamed to \"{nt}\""); }
        if (Str(a, "dueDate") is string dd && dd.Length > 0) { task.DueDate = dd; changes.Add($"due {dd}"); }
        if (Str(a, "startTime") is string st && st.Length > 0) { task.StartTime = st; changes.Add($"start {st}"); }
        if (Str(a, "endTime") is string et && et.Length > 0) { task.EndTime = et; changes.Add($"end {et}"); }
        if (ValidStatuses.Contains(Str(a, "status"))) { task.Status = Str(a, "status")!; changes.Add($"status {task.Status}"); }
        if (ValidPriorities.Contains(Str(a, "priority"))) { task.Priority = Str(a, "priority")!; changes.Add($"priority {task.Priority}"); }
        if (changes.Count == 0) return ("Nothing to change — tell me what to update (time, status, assignee, project…).", false);
        await _db.SaveChangesAsync();
        return ($"✅ Updated \"{task.Title}\": {string.Join(", ", changes)}.", true);
    }

    // Finds a task by (fuzzy) title. Returns the TaskItem, or an error string.
    private async Task<object> FindTask(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "Which task do you mean?";
        var all = await _db.Tasks.OrderByDescending(t => t.Id).ToListAsync();
        var t = all.FirstOrDefault(x => x.Title.Equals(title, StringComparison.OrdinalIgnoreCase))
                ?? all.FirstOrDefault(x => x.Title.Contains(title!, StringComparison.OrdinalIgnoreCase));
        return (object?)t ?? $"No task matching \"{title}\" found.";
    }

    private async Task<(string, bool)> TaskDetail(JsonElement a, List<Project> projects)
    {
        var found = await FindTask(Str(a, "taskTitle"));
        if (found is string err) return (err, false);
        var t = (TaskItem)found;
        var emp = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == t.AssigneeId);
        var proj = projects.FirstOrDefault(p => p.Id == t.ProjectId);
        var lines = new List<string>
        {
            $"📌 {t.Title}",
            $"Status: {t.Status} · Priority: {t.Priority}",
            $"Assignee: {emp?.Name ?? "unassigned"}",
            $"Project: {proj?.Name ?? "none"}",
            $"Due: {t.DueDate ?? "none"}",
            $"Time: {(t.StartTime != null ? $"{t.StartTime} – {t.EndTime}" : "none")}",
        };
        return (string.Join("\n", lines), false);
    }

    private async Task<(string, bool)> DeleteTask(JsonElement a)
    {
        var found = await FindTask(Str(a, "taskTitle"));
        if (found is string err) return (err, false);
        var t = (TaskItem)found;
        _db.Tasks.Remove(t);
        await _db.SaveChangesAsync();
        return ($"🗑️ Deleted task \"{t.Title}\".", true);
    }

    // Open (not Done) task count per employee — busiest first.
    private async Task<(string, bool)> Workload(JsonElement a, List<Employee> emps)
    {
        var one = MatchEmployee(Str(a, "assignee"), emps);
        var open = await _db.Tasks.AsNoTracking().Where(t => t.Status != "Done").ToListAsync();
        if (one != null)
        {
            var n = open.Count(t => t.AssigneeId == one.Id);
            return ($"{one.Name} has {n} open task(s).", false);
        }
        var byEmp = open.GroupBy(t => t.AssigneeId)
            .Select(g => (Name: emps.FirstOrDefault(e => e.Id == g.Key)?.Name ?? $"#{g.Key}", Count: g.Count()))
            .OrderByDescending(x => x.Count).Take(10).ToList();
        if (byEmp.Count == 0) return ("No open tasks right now.", false);
        var lines = byEmp.Select(x => $"• {x.Name}: {x.Count} open");
        return ($"Workload (open tasks):\n{string.Join("\n", lines)}", false);
    }

    // Add a text comment to a task (stored as Quill Delta so the app renders it).
    private async Task<(string, bool)> AddComment(JsonElement a, List<Employee> emps, int byEmpId)
    {
        var title = Str(a, "taskTitle");
        var text = Str(a, "comment");
        if (string.IsNullOrWhiteSpace(title)) return ("Which task should I comment on?", false);
        if (string.IsNullOrWhiteSpace(text)) return ("What should the comment say?", false);
        var all = await _db.Tasks.OrderByDescending(t => t.Id).ToListAsync();
        var t = all.FirstOrDefault(x => x.Title.Equals(title, StringComparison.OrdinalIgnoreCase))
                ?? all.FirstOrDefault(x => x.Title.Contains(title!, StringComparison.OrdinalIgnoreCase));
        if (t == null) return ($"I couldn't find a task named \"{title}\".", false);
        var author = emps.FirstOrDefault(e => e.Id == byEmpId);
        var body = JsonSerializer.Serialize(new object[] { new { insert = text + "\n" } });
        _db.TaskComments.Add(new TaskComment
        {
            TaskId = t.Id,
            AuthorId = byEmpId,
            AuthorName = author?.Name ?? $"#{byEmpId}",
            Body = body,
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
        return ($"✅ Comment added to \"{t.Title}\".", true);
    }

    // Bulk move: e.g. "move all of Amit's ToDo tasks to Done".
    private async Task<(string, bool)> BulkChange(JsonElement a, List<Employee> emps, List<Project> projects)
    {
        var to = Str(a, "toStatus");
        if (!ValidStatuses.Contains(to)) return ("Which status should I move them to? (ToDo/InProgress/Review/Done)", false);
        var emp = MatchEmployee(Str(a, "assignee"), emps);
        var proj = MatchProject(Str(a, "project"), projects);
        var from = ValidStatuses.Contains(Str(a, "fromStatus")) ? Str(a, "fromStatus") : null;
        var q = _db.Tasks.AsQueryable();
        if (emp != null) q = q.Where(t => t.AssigneeId == emp.Id);
        if (proj != null) q = q.Where(t => t.ProjectId == proj.Id);
        if (from != null) q = q.Where(t => t.Status == from);
        var list = await q.Where(t => t.Status != to).ToListAsync();
        if (list.Count == 0) return ("No matching tasks found to move.", false);
        foreach (var t in list) t.Status = to!;
        await _db.SaveChangesAsync();
        var who = emp != null ? $"{emp.Name}'s " : "";
        var fromTxt = from != null ? $"{from} " : "";
        return ($"✅ Moved {list.Count} {who}{fromTxt}task(s) to {to}.", true);
    }

    private async Task<(string, bool)> CountTasks(JsonElement a, List<Employee> emps, List<Project> projects)
    {
        var emp = MatchEmployee(Str(a, "assignee"), emps);
        var proj = MatchProject(Str(a, "project"), projects);
        var status = ValidStatuses.Contains(Str(a, "status")) ? Str(a, "status") : null;
        var priority = ValidPriorities.Contains(Str(a, "priority")) ? Str(a, "priority") : null;
        var q = _db.Tasks.AsQueryable();
        if (emp != null) q = q.Where(t => t.AssigneeId == emp.Id);
        if (proj != null) q = q.Where(t => t.ProjectId == proj.Id);
        if (status != null) q = q.Where(t => t.Status == status);
        if (priority != null) q = q.Where(t => t.Priority == priority);
        var n = await q.CountAsync();
        var who = emp != null ? emp.Name : proj != null ? proj.Name : "everyone";
        var parts = new List<string>();
        if (priority != null) parts.Add($"{priority} priority");
        if (status != null) parts.Add(status!);
        var where = parts.Count > 0 ? $" ({string.Join(", ", parts)})" : "";
        return ($"{who} has {n} task(s){where}.", false);
    }

    private async Task<(string, bool)> Progress(JsonElement a, List<Employee> emps, List<Project> projects)
    {
        var emp = MatchEmployee(Str(a, "assignee"), emps);
        var proj = MatchProject(Str(a, "project"), projects);
        var q = _db.Tasks.AsQueryable();
        if (emp != null) q = q.Where(t => t.AssigneeId == emp.Id);
        if (proj != null) q = q.Where(t => t.ProjectId == proj.Id);
        var total = await q.CountAsync();
        if (total == 0) return ("No tasks found for that.", false);
        var done = await q.CountAsync(t => t.Status == "Done");
        var pct = (int)Math.Round(done * 100.0 / total);
        var scope = emp != null ? $"{emp.Name}'s" : proj != null ? $"{proj.Name}" : "Overall";
        return ($"{scope} progress: {done}/{total} done ({pct}%).", false);
    }

    private async Task<(string, bool)> ListTasks(JsonElement a, List<Employee> emps, List<Project> projects, string? nowIso)
    {
        var emp = MatchEmployee(Str(a, "assignee"), emps);
        var proj = MatchProject(Str(a, "project"), projects);
        var status = ValidStatuses.Contains(Str(a, "status")) ? Str(a, "status") : null;
        var priority = ValidPriorities.Contains(Str(a, "priority")) ? Str(a, "priority") : null;
        var q = _db.Tasks.AsNoTracking().Include(t => t.Assignee).AsQueryable();
        if (emp != null) q = q.Where(t => t.AssigneeId == emp.Id);
        if (proj != null) q = q.Where(t => t.ProjectId == proj.Id);
        if (status != null) q = q.Where(t => t.Status == status);
        if (priority != null) q = q.Where(t => t.Priority == priority);
        if (Bool(a, "noProject")) q = q.Where(t => t.ProjectId == null);
        if (Bool(a, "overdue"))
        {
            var today = NowOf(nowIso).ToString("yyyy-MM-dd");
            q = q.Where(t => t.DueDate != null && string.Compare(t.DueDate, today) < 0 && t.Status != "Done");
        }
        var list = await q.OrderByDescending(t => t.Id).Take(60).ToListAsync();
        if (Bool(a, "noAssignee"))
        {
            var ids = emps.Select(e => e.Id).ToHashSet();
            list = list.Where(t => !ids.Contains(t.AssigneeId)).ToList();
        }
        list = list.Take(20).ToList();
        if (list.Count == 0) return ("No matching tasks found.", false);
        var lines = list.Select(t =>
            $"• {t.Title} [{t.Status}] — {t.Assignee?.Name ?? "unassigned"}{(t.DueDate != null ? $" · due {t.DueDate}" : "")}");
        return ($"{list.Count} task(s):\n{string.Join("\n", lines)}", false);
    }

    private async Task<(string, bool)> ProjectMembers(JsonElement a, List<Project> projects)
    {
        var proj = MatchProject(Str(a, "project"), projects);
        var tasks = await _db.Tasks.AsNoTracking().Include(t => t.Assignee).Include(t => t.Project)
            .Where(t => proj == null || t.ProjectId == proj.Id)
            .ToListAsync();
        if (tasks.Count == 0) return ("No one is assigned to that yet.", false);
        var byProject = tasks.Where(t => t.Project != null)
            .GroupBy(t => t.Project!.Name)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(t => t.Assignee?.Name).Where(n => n != null).Distinct())}");
        var noProj = tasks.Where(t => t.Project == null).Select(t => t.Assignee?.Name).Where(n => n != null).Distinct().ToList();
        var parts = byProject.ToList();
        if (noProj.Count > 0) parts.Add($"(no project): {string.Join(", ", noProj)}");
        return (string.Join("\n", parts), false);
    }

    // ===== Helpers =====
    private static Employee? MatchEmployee(string? name, List<Employee> emps)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var n = name.Trim();
        return emps.FirstOrDefault(e => e.Name.Equals(n, StringComparison.OrdinalIgnoreCase))
            ?? emps.FirstOrDefault(e => e.Code.Equals(n, StringComparison.OrdinalIgnoreCase))
            ?? emps.FirstOrDefault(e => e.Name.Contains(n, StringComparison.OrdinalIgnoreCase))
            ?? emps.FirstOrDefault(e => n.Contains(e.Name.Split(' ')[0], StringComparison.OrdinalIgnoreCase));
    }

    private static Project? MatchProject(string? name, List<Project> projects)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var n = name.Trim();
        return projects.FirstOrDefault(p => p.Name.Equals(n, StringComparison.OrdinalIgnoreCase))
            ?? projects.FirstOrDefault(p => p.Name.Contains(n, StringComparison.OrdinalIgnoreCase));
    }

    private static string? Str(JsonElement e, string prop)
    {
        if (!e.TryGetProperty(prop, out var v)) return null;
        if (v.ValueKind == JsonValueKind.String) { var s = v.GetString(); return string.IsNullOrWhiteSpace(s) || s == "null" ? null : s; }
        if (v.ValueKind == JsonValueKind.Number) return v.ToString();
        return null;
    }

    private static bool Bool(JsonElement e, string prop)
    {
        if (!e.TryGetProperty(prop, out var v)) return false;
        if (v.ValueKind == JsonValueKind.True) return true;
        if (v.ValueKind == JsonValueKind.String) return v.GetString()?.ToLowerInvariant() is "true" or "yes" or "1";
        return false;
    }

    private static string ExtractJson(string s)
    {
        var i = s.IndexOf('{');
        var j = s.LastIndexOf('}');
        return (i >= 0 && j > i) ? s.Substring(i, j - i + 1) : s;
    }

    private static string? Env(string k) => Environment.GetEnvironmentVariable(k);
}
