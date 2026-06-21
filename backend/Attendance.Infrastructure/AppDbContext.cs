using Attendance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Attendance.Infrastructure;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<AttendancePunch> Punches => Set<AttendancePunch>();
    public DbSet<AttendanceDay> Days => Set<AttendanceDay>();
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<LeaveRequest> Leaves => Set<LeaveRequest>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();
    public DbSet<Page> Pages => Set<Page>();
    public DbSet<RolePagePermission> RolePagePermissions => Set<RolePagePermission>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<TaskAttachment> TaskAttachments => Set<TaskAttachment>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // ---- Value converters ----
        var intListConverter = new ValueConverter<List<int>, string>(
            v => string.Join(',', v),
            v => string.IsNullOrWhiteSpace(v)
                ? new List<int>()
                : v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList());

        var intListComparer = new ValueComparer<List<int>>(
            (a, c) => (a ?? new()).SequenceEqual(c ?? new()),
            v => v == null ? 0 : v.Aggregate(0, (h, x) => HashCode.Combine(h, x)),
            v => v.ToList());

        // Face descriptors: list of 128-d embeddings persisted as a JSON string (SQLite text).
        var faceDescriptorsConverter = new ValueConverter<List<List<double>>?, string?>(
            v => v == null ? null : System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
            v => string.IsNullOrWhiteSpace(v)
                ? null
                : System.Text.Json.JsonSerializer.Deserialize<List<List<double>>>(v, (System.Text.Json.JsonSerializerOptions?)null));

        var faceDescriptorsComparer = new ValueComparer<List<List<double>>?>(
            (a, c) => (a == null && c == null) ||
                      (a != null && c != null && a.Count == c.Count &&
                       a.Zip(c, (x, y) => x.SequenceEqual(y)).All(eq => eq)),
            v => v == null ? 0 : v.Aggregate(0, (h, inner) => inner.Aggregate(h, (h2, x) => HashCode.Combine(h2, x))),
            v => v == null ? null : v.Select(inner => inner.ToList()).ToList());

        // ---- Shift ----
        b.Entity<Shift>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired();
            e.Property(x => x.WeeklyOffDays)
                .HasConversion(intListConverter)
                .Metadata.SetValueComparer(intListComparer);
        });

        // ---- Role ----
        b.Entity<Role>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
        });

        // ---- Employee ----
        b.Entity<Employee>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Name).IsRequired();
            e.Property(x => x.MonthlySalary).HasColumnType("decimal(18,2)");
            e.Ignore(x => x.HasFace);
            e.Ignore(x => x.FaceCount);
            e.Property(x => x.FaceDescriptors)
                .HasConversion(faceDescriptorsConverter)
                .Metadata.SetValueComparer(faceDescriptorsComparer);
            e.HasOne(x => x.Shift)
                .WithMany()
                .HasForeignKey(x => x.ShiftId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Role)
                .WithMany()
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ---- AttendancePunch ----
        b.Entity<AttendancePunch>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Direction).HasConversion<string>();
            e.Property(x => x.Source).HasConversion<string>();
            e.HasOne(x => x.Employee)
                .WithMany()
                .HasForeignKey(x => x.EmployeeId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.EmployeeId, x.Timestamp });
        });

        // ---- AttendanceDay ----
        b.Entity<AttendanceDay>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>();
            e.HasOne(x => x.Employee)
                .WithMany()
                .HasForeignKey(x => x.EmployeeId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.EmployeeId, x.Date }).IsUnique();
        });

        // ---- Holiday ----
        b.Entity<Holiday>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired();
            e.HasIndex(x => x.Date);
        });

        // ---- LeaveRequest ----
        b.Entity<LeaveRequest>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasConversion<string>();
            e.Property(x => x.Status).HasConversion<string>();
            e.HasOne(x => x.Employee)
                .WithMany()
                .HasForeignKey(x => x.EmployeeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ---- AppSetting ----
        b.Entity<AppSetting>(e =>
        {
            e.HasKey(x => x.Id);
        });

        // ---- Page ----
        b.Entity<Page>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Key).IsRequired();
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Name).IsRequired();
            e.Property(x => x.Route).IsRequired();
        });

        // ---- RolePagePermission ----
        b.Entity<RolePagePermission>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Role)
                .WithMany()
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Page)
                .WithMany()
                .HasForeignKey(x => x.PageId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.RoleId, x.PageId }).IsUnique();
        });

        // ---- TaskItem (table "Tasks") ----
        // Two FKs to Employee (Assignee + AssignedBy). Use NoAction so EF doesn't
        // try to create multiple cascade paths (which SQL Server/PG reject) and so
        // deleting an employee never silently nukes tasks. Nav is optional (for Include).
        b.Entity<TaskItem>(e =>
        {
            e.ToTable("Tasks");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).IsRequired();
            e.Property(x => x.Status).IsRequired();
            e.Property(x => x.Priority).IsRequired();
            e.HasOne(x => x.Assignee)
                .WithMany()
                .HasForeignKey(x => x.AssigneeId)
                .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.AssignedBy)
                .WithMany()
                .HasForeignKey(x => x.AssignedById)
                .OnDelete(DeleteBehavior.NoAction);
            // Optional project link. NoAction: deleting a project does NOT auto-null/cascade
            // tasks at the DB level — the controller handles project-delete cascade explicitly.
            e.HasOne(x => x.Project)
                .WithMany()
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // ---- Project (table "Projects") ----
        // CreatedBy -> Employee uses NoAction so deleting an employee never silently
        // nukes projects (and to avoid multiple cascade paths).
        b.Entity<Project>(e =>
        {
            e.ToTable("Projects");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired();
            e.HasOne(x => x.CreatedBy)
                .WithMany()
                .HasForeignKey(x => x.CreatedById)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // ---- TaskAttachment (table "TaskAttachments") ----
        // FK TaskId -> Tasks.Id with CASCADE: deleting a task removes its attachments.
        // SQLite (EnsureCreated in tests) honours this cascade too.
        b.Entity<TaskAttachment>(e =>
        {
            e.ToTable("TaskAttachments");
            e.HasKey(x => x.Id);
            e.Property(x => x.FileName).IsRequired();
            e.Property(x => x.MimeType).IsRequired();
            e.Property(x => x.DataBase64).IsRequired();
            e.HasOne(x => x.Task)
                .WithMany()
                .HasForeignKey(x => x.TaskId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
