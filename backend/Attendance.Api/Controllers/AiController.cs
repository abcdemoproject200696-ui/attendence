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
}

public record AiResult(bool Ok, string Reply, string Provider, bool Changed);

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

    // Temporary diagnostic: calls Gemini with a trivial prompt and returns the raw
    // HTTP status + response body so we can see WHY it fails (never returns the key).
    [HttpGet("diag")]
    public async Task<ActionResult<object>> Diag()
    {
        var key = Env("GEMINI_API_KEY");
        if (string.IsNullOrWhiteSpace(key)) return Ok(new { error = "GEMINI_API_KEY not set on server" });
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={key}";
        var body = JsonSerializer.Serialize(new { contents = new[] { new { parts = new[] { new { text = "say hi" } } } } });
        try
        {
            var res = await _http.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"));
            var text = await res.Content.ReadAsStringAsync();
            return Ok(new { keyPrefix = key.Length > 6 ? key[..6] : key, keyLen = key.Length, status = (int)res.StatusCode, body = text.Length > 600 ? text[..600] : text });
        }
        catch (Exception e) { return Ok(new { error = e.Message }); }
    }

    [HttpPost("command")]
    public async Task<ActionResult<AiResult>> Command(AiCommandDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Text))
            return Ok(new AiResult(false, "Please type or say a command.", "", false));

        var employees = await _db.Employees.AsNoTracking().Include(e => e.Role).ToListAsync();
        var projects = await _db.Projects.AsNoTracking().ToListAsync();
        var prompt = BuildPrompt(dto.Text, employees, projects);

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

        var (reply, changed) = await Execute(action, employees, projects, dto.ByEmpId);
        return Ok(new AiResult(true, reply, used, changed));
    }

    // ===== Prompt =====
    private static string BuildPrompt(string text, List<Employee> emps, List<Project> projects)
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var empList = string.Join(", ", emps.Select(e => $"{e.Name} ({e.Code})"));
        var projList = projects.Count == 0 ? "(none)" : string.Join(", ", projects.Select(p => p.Name));
        var sb = new StringBuilder();
        sb.AppendLine("You are a task-management assistant for a Kanban app. Convert the user's request into ONE JSON action. Reply with ONLY a JSON object, no prose, no markdown.");
        sb.AppendLine($"Today is {today} (UTC). Statuses: ToDo, InProgress, Review, Done. Priorities: Low, Medium, High, Urgent.");
        sb.AppendLine($"Employees: {empList}");
        sb.AppendLine($"Projects: {projList}");
        sb.AppendLine("Use the employee's real name from the list (match nicknames/first names to it). Dates as yyyy-MM-dd, date-times as yyyy-MM-ddTHH:mm.");
        sb.AppendLine("Choose ONE action and return its JSON:");
        sb.AppendLine("- Create/assign a task: {\"action\":\"create_task\",\"title\":\"...\",\"assignee\":\"<name>\",\"priority\":\"Medium\",\"status\":\"ToDo\",\"dueDate\":null,\"startTime\":null,\"endTime\":null,\"project\":null}");
        sb.AppendLine("- Change a task's time/date/status: {\"action\":\"change_task\",\"taskTitle\":\"...\",\"dueDate\":null,\"startTime\":null,\"endTime\":null,\"status\":null,\"priority\":null}");
        sb.AppendLine("- Count tasks: {\"action\":\"count_tasks\",\"assignee\":null,\"status\":null}  (null = all)");
        sb.AppendLine("- Progress percent (done vs total): {\"action\":\"progress\",\"assignee\":null,\"project\":null}");
        sb.AppendLine("- List tasks: {\"action\":\"list_tasks\",\"assignee\":null,\"status\":null,\"project\":null}");
        sb.AppendLine("- Who works on a project: {\"action\":\"project_members\",\"project\":null}  (null = all projects)");
        sb.AppendLine("- Anything else / a question you can answer directly: {\"action\":\"chat\",\"reply\":\"...\"}");
        sb.AppendLine($"User request: {text}");
        return sb.ToString();
    }

    // ===== Provider calls (return the model's text, or null on failure) =====
    private static async Task<string?> CallGemini(string prompt)
    {
        var key = Env("GEMINI_API_KEY");
        if (string.IsNullOrWhiteSpace(key)) return null;
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={key}";
        var body = JsonSerializer.Serialize(new
        {
            contents = new[] { new { parts = new[] { new { text = prompt } } } },
            generationConfig = new { responseMimeType = "application/json", temperature = 0.2 }
        });
        var res = await _http.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"));
        if (!res.IsSuccessStatusCode) throw new Exception($"gemini {(int)res.StatusCode}");
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("candidates")[0].GetProperty("content")
            .GetProperty("parts")[0].GetProperty("text").GetString();
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
    private async Task<(string reply, bool changed)> Execute(
        JsonElement a, List<Employee> emps, List<Project> projects, int byEmpId)
    {
        var action = Str(a, "action")?.ToLowerInvariant() ?? "chat";
        switch (action)
        {
            case "create_task":
                return await CreateTask(a, emps, projects, byEmpId);
            case "change_task":
                return await ChangeTask(a);
            case "count_tasks":
                return await CountTasks(a, emps);
            case "progress":
                return await Progress(a, emps, projects);
            case "list_tasks":
                return await ListTasks(a, emps, projects);
            case "project_members":
                return await ProjectMembers(a, projects);
            default:
                return (Str(a, "reply") ?? "Sorry, I couldn't understand that. Try: \"assign a task to <name>\".", false);
        }
    }

    private async Task<(string, bool)> CreateTask(JsonElement a, List<Employee> emps, List<Project> projects, int byEmpId)
    {
        var title = Str(a, "title");
        if (string.IsNullOrWhiteSpace(title)) return ("What should the task be called?", false);
        var assignee = MatchEmployee(Str(a, "assignee"), emps);
        if (assignee == null) return ($"I couldn't find an employee called \"{Str(a, "assignee")}\".", false);
        var project = MatchProject(Str(a, "project"), projects);
        var status = ValidStatuses.Contains(Str(a, "status")) ? Str(a, "status")! : "ToDo";
        var priority = ValidPriorities.Contains(Str(a, "priority")) ? Str(a, "priority")! : "Medium";
        var by = byEmpId > 0 ? byEmpId : assignee.Id;

        var t = new TaskItem
        {
            Title = title!,
            AssigneeId = assignee.Id,
            AssignedById = by,
            Status = status,
            Priority = priority,
            DueDate = Str(a, "dueDate"),
            StartTime = Str(a, "startTime"),
            EndTime = Str(a, "endTime"),
            ProjectId = project?.Id,
            CreatedAt = DateTime.UtcNow,
        };
        _db.Tasks.Add(t);
        await _db.SaveChangesAsync();
        var extra = new List<string> { priority, status };
        if (t.DueDate != null) extra.Add($"due {t.DueDate}");
        if (project != null) extra.Add(project.Name);
        return ($"✅ Task \"{title}\" assigned to {assignee.Name} ({string.Join(", ", extra)}).", true);
    }

    private async Task<(string, bool)> ChangeTask(JsonElement a)
    {
        var title = Str(a, "taskTitle");
        if (string.IsNullOrWhiteSpace(title)) return ("Which task should I change?", false);
        var all = await _db.Tasks.OrderByDescending(t => t.Id).ToListAsync();
        var t = all.FirstOrDefault(x => x.Title.Equals(title, StringComparison.OrdinalIgnoreCase))
                ?? all.FirstOrDefault(x => x.Title.Contains(title!, StringComparison.OrdinalIgnoreCase));
        if (t == null) return ($"No task matching \"{title}\" found.", false);

        var changes = new List<string>();
        if (Str(a, "dueDate") is string dd && dd.Length > 0) { t.DueDate = dd; changes.Add($"due {dd}"); }
        if (Str(a, "startTime") is string st && st.Length > 0) { t.StartTime = st; changes.Add($"start {st}"); }
        if (Str(a, "endTime") is string et && et.Length > 0) { t.EndTime = et; changes.Add($"end {et}"); }
        if (ValidStatuses.Contains(Str(a, "status"))) { t.Status = Str(a, "status")!; changes.Add($"status {t.Status}"); }
        if (ValidPriorities.Contains(Str(a, "priority"))) { t.Priority = Str(a, "priority")!; changes.Add($"priority {t.Priority}"); }
        if (changes.Count == 0) return ("Nothing to change — tell me the new time/status.", false);
        await _db.SaveChangesAsync();
        return ($"✅ Updated \"{t.Title}\": {string.Join(", ", changes)}.", true);
    }

    private async Task<(string, bool)> CountTasks(JsonElement a, List<Employee> emps)
    {
        var emp = MatchEmployee(Str(a, "assignee"), emps);
        var status = ValidStatuses.Contains(Str(a, "status")) ? Str(a, "status") : null;
        var q = _db.Tasks.AsQueryable();
        if (emp != null) q = q.Where(t => t.AssigneeId == emp.Id);
        if (status != null) q = q.Where(t => t.Status == status);
        var n = await q.CountAsync();
        var who = emp != null ? emp.Name : "everyone";
        var where = status != null ? $" in {status}" : "";
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

    private async Task<(string, bool)> ListTasks(JsonElement a, List<Employee> emps, List<Project> projects)
    {
        var emp = MatchEmployee(Str(a, "assignee"), emps);
        var proj = MatchProject(Str(a, "project"), projects);
        var status = ValidStatuses.Contains(Str(a, "status")) ? Str(a, "status") : null;
        var q = _db.Tasks.AsQueryable();
        if (emp != null) q = q.Where(t => t.AssigneeId == emp.Id);
        if (proj != null) q = q.Where(t => t.ProjectId == proj.Id);
        if (status != null) q = q.Where(t => t.Status == status);
        var list = await q.OrderByDescending(t => t.Id).Take(15).ToListAsync();
        if (list.Count == 0) return ("No matching tasks.", false);
        var lines = list.Select(t => $"• {t.Title} [{t.Status}]{(t.DueDate != null ? $" · due {t.DueDate}" : "")}");
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

    private static string ExtractJson(string s)
    {
        var i = s.IndexOf('{');
        var j = s.LastIndexOf('}');
        return (i >= 0 && j > i) ? s.Substring(i, j - i + 1) : s;
    }

    private static string? Env(string k) => Environment.GetEnvironmentVariable(k);
}
