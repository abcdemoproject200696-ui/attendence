using System.Text.Json.Serialization;
using Attendance.Api.Services;
using Attendance.Infrastructure;
using Microsoft.EntityFrameworkCore;

// PostgreSQL (Npgsql 6+) rejects non-UTC DateTimes for 'timestamp with time zone'.
// This app uses local/Unspecified DateTimes (DateTime.Now, date-only values), so we
// opt into legacy behavior — DateTimes map to 'timestamp without time zone' and any
// Kind is accepted. MUST run before any Npgsql/DbContext usage.
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

const string CorsPolicy = "FrontendCors";

// ---- Container hosting: bind to the port Render injects via PORT (if present). ----
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Allow large attachment uploads (videos). A ~50MB file is ~70MB once base64'd
// inside the JSON body, so raise Kestrel's default 30MB cap. Keep in sync with
// the ~70M-char limit in AttachmentsController.
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 80_000_000);

// ---- EF Core: pick provider at runtime ----
// PostgreSQL (production / Render) if DATABASE_URL or ConnectionStrings__DefaultConnection is set,
// otherwise SQLite local fallback (no DB install needed for `dotnet run`).
var databaseUrl = builder.Configuration["DATABASE_URL"]
                  ?? Environment.GetEnvironmentVariable("DATABASE_URL");
var explicitConn = builder.Configuration.GetConnectionString("DefaultConnection");
var dbConfig = DbConnectionHelper.Resolve(databaseUrl, explicitConn);

builder.Services.AddDbContext<AppDbContext>(opt =>
{
    if (dbConfig.Provider == DbProvider.PostgreSql)
        opt.UseNpgsql(dbConfig.ConnectionString);
    else
        opt.UseSqlite(dbConfig.ConnectionString);
});

// ---- App services ----
builder.Services.AddScoped<AttendanceService>();
builder.Services.AddScoped<PermissionService>();
builder.Services.AddSingleton<OtpService>();
builder.Services.AddSingleton<Attendance.Api.Services.EmailSender>();
builder.Services.AddSingleton<Attendance.Api.Services.PushSender>();

// ---- Controllers + JSON (camelCase default; enums as strings) ----
builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// ---- Swagger / OpenAPI ----
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ---- CORS ----
// If FRONTEND_ORIGINS (comma-separated) is set, restrict to those origins.
// Otherwise allow any origin (convenient for this internal tool + dev).
var frontendOrigins = (builder.Configuration["FRONTEND_ORIGINS"]
                       ?? Environment.GetEnvironmentVariable("FRONTEND_ORIGINS"))
    ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, policy =>
    {
        // ExposeHeaders lets the browser READ our custom "X-Account-Inactive"
        // response header (CORS hides non-safelisted headers otherwise).
        if (frontendOrigins is { Length: > 0 })
            policy.WithOrigins(frontendOrigins).AllowAnyHeader().AllowAnyMethod().WithExposedHeaders("X-Account-Inactive");
        else
            policy.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowAnyMethod().WithExposedHeaders("X-Account-Inactive");
    });
});

var app = builder.Build();

// ---- Log which DB provider is in use ----
app.Logger.LogInformation("DB provider: {Provider}",
    dbConfig.Provider == DbProvider.PostgreSql ? "PostgreSQL" : "SQLite");

// ---- Ensure DB created + seeded ----
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();

    // Lightweight migration: EnsureCreated does NOT add new columns to an already
    // existing database, so columns added after the first deploy are patched here.
    if (dbConfig.Provider == DbProvider.PostgreSql)
    {
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Settings\" ADD COLUMN IF NOT EXISTS \"VoiceEnabled\" boolean NOT NULL DEFAULT TRUE;");
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Settings\" ADD COLUMN IF NOT EXISTS \"OvertimePayable\" boolean NOT NULL DEFAULT FALSE;");
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Settings\" ADD COLUMN IF NOT EXISTS \"HrCanEditAttendance\" boolean NOT NULL DEFAULT FALSE;");
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Settings\" ADD COLUMN IF NOT EXISTS \"TaskAssignEmail\" boolean NOT NULL DEFAULT FALSE;");
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Settings\" ADD COLUMN IF NOT EXISTS \"SignupOtpEmail\" boolean NOT NULL DEFAULT FALSE;");
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Employees\" ADD COLUMN IF NOT EXISTS \"Gender\" text;");
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Employees\" ADD COLUMN IF NOT EXISTS \"BloodGroup\" text;");
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Employees\" ADD COLUMN IF NOT EXISTS \"Dob\" text;");
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Employees\" ADD COLUMN IF NOT EXISTS \"PhotoUrl\" text;");
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Employees\" ADD COLUMN IF NOT EXISTS \"FirstName\" text;");
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Employees\" ADD COLUMN IF NOT EXISTS \"LastName\" text;");
        // Kanban tasks table (EnsureCreated never adds new tables to an existing DB).
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS \"Tasks\" (" +
            "\"Id\" serial PRIMARY KEY, " +
            "\"Title\" text NOT NULL, " +
            "\"Description\" text NULL, " +
            "\"AssigneeId\" integer NOT NULL, " +
            "\"AssignedById\" integer NOT NULL, " +
            "\"Status\" text NOT NULL, " +
            "\"Priority\" text NOT NULL, " +
            "\"DueDate\" text NULL, " +
            "\"CreatedAt\" timestamptz NOT NULL);");
        // New task columns (project link + time window). IF NOT EXISTS keeps it idempotent.
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Tasks\" ADD COLUMN IF NOT EXISTS \"ProjectId\" integer NULL;");
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Tasks\" ADD COLUMN IF NOT EXISTS \"StartTime\" text NULL;");
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Tasks\" ADD COLUMN IF NOT EXISTS \"EndTime\" text NULL;");
        // Projects table.
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS \"Projects\" (" +
            "\"Id\" serial PRIMARY KEY, " +
            "\"Name\" text NOT NULL, " +
            "\"Description\" text NULL, " +
            "\"Status\" text NOT NULL DEFAULT 'Active', " +
            "\"CreatedById\" integer NOT NULL, " +
            "\"CreatedAt\" timestamptz NOT NULL);");
        // In case "Projects" already exists without Status.
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Projects\" ADD COLUMN IF NOT EXISTS \"Status\" text NOT NULL DEFAULT 'Active';");
        // Task attachments table.
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS \"TaskAttachments\" (" +
            "\"Id\" serial PRIMARY KEY, " +
            "\"TaskId\" integer NOT NULL, " +
            "\"FileName\" text NOT NULL, " +
            "\"MimeType\" text NOT NULL, " +
            "\"DataBase64\" text NOT NULL, " +
            "\"CreatedAt\" timestamptz NOT NULL);");
        // Task comments table (rich-text body, may embed small base64 images).
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS \"TaskComments\" (" +
            "\"Id\" serial PRIMARY KEY, " +
            "\"TaskId\" integer NOT NULL, " +
            "\"AuthorId\" integer NOT NULL, " +
            "\"AuthorName\" text NOT NULL, " +
            "\"Body\" text NOT NULL, " +
            "\"CreatedAt\" timestamptz NOT NULL);");
        // Threaded replies: parent comment id (added after first deploy).
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"TaskComments\" ADD COLUMN IF NOT EXISTS \"ParentId\" integer NULL;");
        // Signup email OTP codes (persisted so they survive a server restart).
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS \"SignupOtps\" (" +
            "\"Id\" serial PRIMARY KEY, " +
            "\"Email\" text NOT NULL, " +
            "\"Code\" text NOT NULL, " +
            "\"CreatedAt\" timestamptz NOT NULL);");
        // FCM device tokens for push notifications.
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS \"DeviceTokens\" (" +
            "\"Id\" serial PRIMARY KEY, " +
            "\"EmployeeId\" integer NOT NULL, " +
            "\"Token\" text NOT NULL, " +
            "\"Platform\" text NULL, " +
            "\"UpdatedAt\" timestamptz NOT NULL);");
    }
    else
    {
        // SQLite (local dev) has no ADD COLUMN IF NOT EXISTS — ignore "already exists".
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"Settings\" ADD COLUMN \"VoiceEnabled\" INTEGER NOT NULL DEFAULT 1;");
        }
        catch { /* column already exists */ }
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"Settings\" ADD COLUMN \"OvertimePayable\" INTEGER NOT NULL DEFAULT 0;");
        }
        catch { /* column already exists */ }
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"Settings\" ADD COLUMN \"HrCanEditAttendance\" INTEGER NOT NULL DEFAULT 0;");
        }
        catch { /* column already exists */ }
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"Settings\" ADD COLUMN \"TaskAssignEmail\" INTEGER NOT NULL DEFAULT 0;");
        }
        catch { /* column already exists */ }
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"Settings\" ADD COLUMN \"SignupOtpEmail\" INTEGER NOT NULL DEFAULT 0;");
        }
        catch { /* column already exists */ }
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"Employees\" ADD COLUMN \"Gender\" TEXT;");
        }
        catch { /* column already exists */ }
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"Employees\" ADD COLUMN \"BloodGroup\" TEXT;");
        }
        catch { /* column already exists */ }
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"Employees\" ADD COLUMN \"Dob\" TEXT;");
        }
        catch { /* column already exists */ }
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"Employees\" ADD COLUMN \"FirstName\" TEXT;");
        }
        catch { /* column already exists */ }
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"Employees\" ADD COLUMN \"LastName\" TEXT;");
        }
        catch { /* column already exists */ }
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"TaskComments\" ADD COLUMN \"ParentId\" INTEGER NULL;");
        }
        catch { /* column already exists */ }
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"Employees\" ADD COLUMN \"PhotoUrl\" TEXT;");
        }
        catch { /* column already exists */ }
        try
        {
            // Kanban tasks table (SQLite local dev). IF NOT EXISTS keeps it idempotent.
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TABLE IF NOT EXISTS \"Tasks\" (" +
                "\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_Tasks\" PRIMARY KEY AUTOINCREMENT, " +
                "\"Title\" TEXT NOT NULL, " +
                "\"Description\" TEXT NULL, " +
                "\"AssigneeId\" INTEGER NOT NULL, " +
                "\"AssignedById\" INTEGER NOT NULL, " +
                "\"Status\" TEXT NOT NULL, " +
                "\"Priority\" TEXT NOT NULL, " +
                "\"DueDate\" TEXT NULL, " +
                "\"CreatedAt\" TEXT NOT NULL);");
        }
        catch { /* table already exists */ }
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"Tasks\" ADD COLUMN \"ProjectId\" INTEGER NULL;");
        }
        catch { /* column already exists */ }
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"Tasks\" ADD COLUMN \"StartTime\" TEXT NULL;");
        }
        catch { /* column already exists */ }
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"Tasks\" ADD COLUMN \"EndTime\" TEXT NULL;");
        }
        catch { /* column already exists */ }
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TABLE IF NOT EXISTS \"Projects\" (" +
                "\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_Projects\" PRIMARY KEY AUTOINCREMENT, " +
                "\"Name\" TEXT NOT NULL, " +
                "\"Description\" TEXT NULL, " +
                "\"Status\" TEXT NOT NULL DEFAULT 'Active', " +
                "\"CreatedById\" INTEGER NOT NULL, " +
                "\"CreatedAt\" TEXT NOT NULL);");
        }
        catch { /* table already exists */ }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Projects\" ADD COLUMN \"Status\" TEXT NOT NULL DEFAULT 'Active';"); } catch { /* column exists */ }
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TABLE IF NOT EXISTS \"TaskAttachments\" (" +
                "\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_TaskAttachments\" PRIMARY KEY AUTOINCREMENT, " +
                "\"TaskId\" INTEGER NOT NULL, " +
                "\"FileName\" TEXT NOT NULL, " +
                "\"MimeType\" TEXT NOT NULL, " +
                "\"DataBase64\" TEXT NOT NULL, " +
                "\"CreatedAt\" TEXT NOT NULL);");
        }
        catch { /* table already exists */ }
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TABLE IF NOT EXISTS \"TaskComments\" (" +
                "\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_TaskComments\" PRIMARY KEY AUTOINCREMENT, " +
                "\"TaskId\" INTEGER NOT NULL, " +
                "\"AuthorId\" INTEGER NOT NULL, " +
                "\"AuthorName\" TEXT NOT NULL, " +
                "\"Body\" TEXT NOT NULL, " +
                "\"CreatedAt\" TEXT NOT NULL);");
        }
        catch { /* table already exists */ }
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TABLE IF NOT EXISTS \"SignupOtps\" (" +
                "\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_SignupOtps\" PRIMARY KEY AUTOINCREMENT, " +
                "\"Email\" TEXT NOT NULL, " +
                "\"Code\" TEXT NOT NULL, " +
                "\"CreatedAt\" TEXT NOT NULL);");
        }
        catch { /* table already exists */ }
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TABLE IF NOT EXISTS \"DeviceTokens\" (" +
                "\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_DeviceTokens\" PRIMARY KEY AUTOINCREMENT, " +
                "\"EmployeeId\" INTEGER NOT NULL, " +
                "\"Token\" TEXT NOT NULL, " +
                "\"Platform\" TEXT NULL, " +
                "\"UpdatedAt\" TEXT NOT NULL);");
        }
        catch { /* table already exists */ }
    }

    await DbSeeder.SeedAsync(db);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(CorsPolicy);

// ---- Real-time active-account gate ----
// No JWT in this app, so each client sends its identity via the "X-Emp-Id" header
// after login. On every (non-auth) request we re-check that the employee is still
// active; the moment an admin sets them inactive, their NEXT call gets 403 + the
// "X-Account-Inactive" header, and the client force-logs-out to the login page.
// Auth endpoints (/api/auth/*) are skipped — login itself already reports inactive,
// and forgot/reset-password already require an active account. Requests without the
// header (anonymous/legacy) pass through unchanged.
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;
    if (!path.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase)
        && context.Request.Headers.TryGetValue("X-Emp-Id", out var idValue)
        && int.TryParse(idValue, out var empId))
    {
        var db = context.RequestServices.GetRequiredService<AppDbContext>();
        var isActive = await db.Employees.AsNoTracking()
            .Where(e => e.Id == empId)
            .Select(e => (bool?)e.IsActive)
            .FirstOrDefaultAsync();
        if (isActive != true)
        {
            context.Response.StatusCode = 403;
            context.Response.Headers["X-Account-Inactive"] = "1";
            await context.Response.WriteAsync("Your account is inactive. Please contact HR.");
            return;
        }
    }
    await next();
});

app.UseAuthorization();
app.MapControllers();

// Lightweight health check for uptime pingers (e.g. UptimeRobot) that keep the
// free Render instance from sleeping. Responds 200 to BOTH GET and HEAD so a
// HEAD-only monitor still shows "Up".
app.MapMethods("/api/health", new[] { "GET", "HEAD" }, () => Results.Ok("OK"));

app.Run();

// Exposed for potential integration testing.
public partial class Program { }
