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
        if (frontendOrigins is { Length: > 0 })
            policy.WithOrigins(frontendOrigins).AllowAnyHeader().AllowAnyMethod();
        else
            policy.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowAnyMethod();
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
            "ALTER TABLE \"Employees\" ADD COLUMN IF NOT EXISTS \"Gender\" text;");
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Employees\" ADD COLUMN IF NOT EXISTS \"BloodGroup\" text;");
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Employees\" ADD COLUMN IF NOT EXISTS \"Dob\" text;");
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Employees\" ADD COLUMN IF NOT EXISTS \"PhotoUrl\" text;");
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
                "ALTER TABLE \"Employees\" ADD COLUMN \"PhotoUrl\" TEXT;");
        }
        catch { /* column already exists */ }
    }

    await DbSeeder.SeedAsync(db);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(CorsPolicy);
app.UseAuthorization();
app.MapControllers();

app.Run();

// Exposed for potential integration testing.
public partial class Program { }
