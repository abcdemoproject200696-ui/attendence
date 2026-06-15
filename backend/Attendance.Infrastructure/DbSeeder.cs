using Attendance.Domain;
using Attendance.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Attendance.Infrastructure;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        // Global settings: ensure exactly one row (Id=1) exists.
        if (!await db.Settings.AnyAsync())
        {
            db.Settings.Add(new AppSetting { Id = 1, FaceMatchThreshold = 0.5, RequireLiveness = false });
            await db.SaveChangesAsync();
        }

        // Roles: software-company designations. Explicit Ids (admin=1, hr=2 as requested).
        if (!await db.Roles.AnyAsync())
        {
            db.Roles.AddRange(
                new Role { Id = 1, Name = "Admin" },
                new Role { Id = 2, Name = "HR" },
                new Role { Id = 3, Name = "Supervisor" },
                new Role { Id = 4, Name = "Project Manager" },
                new Role { Id = 5, Name = "Team Lead" },
                new Role { Id = 6, Name = "Software Engineer" },
                new Role { Id = 7, Name = "Web Developer" },
                new Role { Id = 8, Name = "QA Engineer" },
                new Role { Id = 9, Name = "UI/UX Designer" },
                new Role { Id = 10, Name = "DevOps Engineer" },
                new Role { Id = 11, Name = "Staff" });
            await db.SaveChangesAsync();
        }

        // Pages: explicit ids/keys per CONTRACT.md RBAC section. MenuOrder = Id.
        if (!await db.Pages.AnyAsync())
        {
            db.Pages.AddRange(
                new Page { Id = 1, Key = "dashboard", Name = "Dashboard", Route = "/dashboard", MenuOrder = 1 },
                new Page { Id = 2, Key = "kiosk", Name = "Attendance", Route = "/kiosk", MenuOrder = 2 },
                new Page { Id = 3, Key = "employees", Name = "Employees", Route = "/employees", MenuOrder = 3 },
                new Page { Id = 4, Key = "daily", Name = "Daily Attendance", Route = "/daily", MenuOrder = 4 },
                new Page { Id = 5, Key = "report", Name = "Monthly Report", Route = "/report", MenuOrder = 5 },
                new Page { Id = 6, Key = "holidays", Name = "Holidays", Route = "/holidays", MenuOrder = 6 },
                new Page { Id = 7, Key = "leaves", Name = "Leaves", Route = "/leaves", MenuOrder = 7 },
                new Page { Id = 8, Key = "salary", Name = "Salary", Route = "/salary", MenuOrder = 8 },
                new Page { Id = 9, Key = "settings", Name = "Settings", Route = "/settings", MenuOrder = 9 },
                new Page { Id = 10, Key = "permissions", Name = "Permissions", Route = "/permissions", MenuOrder = 10 });
            await db.SaveChangesAsync();
        }

        // Default role->page permissions (only if none).
        if (!await db.RolePagePermissions.AnyAsync())
        {
            var allPageIds = await db.Pages.Select(p => p.Id).ToListAsync();
            // HR(2): dashboard,kiosk,employees,daily,report,holidays,leaves,salary (not settings/permissions).
            var hrPageIds = new[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            // Everyone else: dashboard,kiosk.
            var basicPageIds = new[] { 1, 2 };

            var perms = new List<RolePagePermission>();
            var roleIds = await db.Roles.Select(r => r.Id).ToListAsync();
            foreach (var roleId in roleIds)
            {
                IEnumerable<int> pageIds = roleId switch
                {
                    1 => allPageIds,          // Admin -> all
                    2 => hrPageIds,           // HR
                    _ => basicPageIds         // others
                };
                foreach (var pageId in pageIds)
                    perms.Add(new RolePagePermission { RoleId = roleId, PageId = pageId });
            }
            db.RolePagePermissions.AddRange(perms);
            await db.SaveChangesAsync();
        }

        if (await db.Shifts.AnyAsync()) return;

        var shift = new Shift
        {
            Name = "General",
            ShiftStart = "10:00",
            ShiftEnd = "19:00",
            RequiredMinutes = 480,
            LunchStart = "13:00",
            LunchEnd = "14:00",
            AutoDeductLunch = true,
            LunchPaid = false,
            GraceMinutes = 5,
            HalfDayThresholdMinutes = 240,
            WeeklyOffDays = new() { 0 } // Sunday off
        };
        db.Shifts.Add(shift);
        await db.SaveChangesAsync();

        var employees = new List<Employee>
        {
            // EMP001 is the Admin (roleId 1) with known creds admin123 (see CONTRACT.md).
            new() { Code = "EMP001", Name = "Aarav Sharma", RoleId = 1, Email = "aarav@example.com", Phone = "9000000001", ShiftId = shift.Id, MonthlySalary = 45000m, IsActive = true, PasswordHash = PasswordHasher.Hash("admin123") },
            new() { Code = "EMP002", Name = "Priya Verma", RoleId = 7, Email = "priya@example.com", Phone = "9000000002", ShiftId = shift.Id, MonthlySalary = 38000m, IsActive = true, PasswordHash = PasswordHasher.Hash("pass123") },
            new() { Code = "EMP003", Name = "Rohan Gupta", RoleId = 3, Email = "rohan@example.com", Phone = "9000000003", ShiftId = shift.Id, MonthlySalary = 55000m, IsActive = true, PasswordHash = PasswordHasher.Hash("pass123") }
        };
        db.Employees.AddRange(employees);

        var year = DateTime.Today.Year;
        var holidays = new List<Holiday>
        {
            new() { Date = new DateTime(year, 1, 26), Name = "Republic Day", IsPaid = true },
            new() { Date = new DateTime(year, 8, 15), Name = "Independence Day", IsPaid = true },
            new() { Date = new DateTime(year, 10, 2), Name = "Gandhi Jayanti", IsPaid = true },
            new() { Date = new DateTime(year, 11, 8), Name = "Diwali", IsPaid = true }
        };
        db.Holidays.AddRange(holidays);

        await db.SaveChangesAsync();
    }
}
