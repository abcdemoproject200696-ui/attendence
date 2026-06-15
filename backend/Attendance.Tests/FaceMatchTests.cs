using Attendance.Api.Services;
using Attendance.Domain.Entities;
using Attendance.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Attendance.Tests;

/// <summary>
/// Exercises <see cref="AttendanceService.MatchFaceAsync"/> against a real SQLite (in-memory)
/// AppDbContext to verify the configurable AppSetting.FaceMatchThreshold and returned distance.
/// </summary>
public class FaceMatchTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;

    public FaceMatchTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_conn).Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    private async Task<Employee> SeedEmployeeAsync(List<double> descriptor)
        => await SeedEmployeeAsync(new List<List<double>> { descriptor });

    private async Task<Employee> SeedEmployeeAsync(List<List<double>> descriptors, string code = "EMP001")
    {
        var shift = await _db.Shifts.FirstOrDefaultAsync();
        if (shift is null)
        {
            shift = new Shift { Name = "General", WeeklyOffDays = new() { 0 } };
            _db.Shifts.Add(shift);
            await _db.SaveChangesAsync();
        }
        if (!await _db.Roles.AnyAsync())
        {
            _db.Roles.Add(new Role { Id = 6, Name = "Software Engineer" });
            await _db.SaveChangesAsync();
        }
        var emp = new Employee
        {
            Code = code, Name = "Aarav", ShiftId = shift.Id, RoleId = 6,
            IsActive = true, FaceDescriptors = descriptors
        };
        _db.Employees.Add(emp);
        await _db.SaveChangesAsync();
        return emp;
    }

    private void SetThreshold(double threshold)
    {
        _db.Settings.Add(new AppSetting { Id = 1, FaceMatchThreshold = threshold });
        _db.SaveChanges();
    }

    [Fact]
    public async Task NearestWithinThreshold_Matches_AndReturnsDistance()
    {
        var enrolled = new List<double> { 0.0, 0.0, 0.0 };
        await SeedEmployeeAsync(enrolled);
        SetThreshold(0.5);
        var svc = new AttendanceService(_db);

        // probe distance = sqrt(0.1^2 *3) ~= 0.173, within 0.5
        var probe = new List<double> { 0.1, 0.1, 0.1 };
        var match = await svc.MatchFaceAsync(probe);

        Assert.NotNull(match.Employee);
        Assert.Equal("EMP001", match.Employee!.Code);
        Assert.True(match.Distance is > 0.17 and < 0.18);
    }

    [Fact]
    public async Task BeyondThreshold_NoMatch_ButDistanceStillReturned()
    {
        await SeedEmployeeAsync(new List<double> { 0.0, 0.0, 0.0 });
        SetThreshold(0.3); // strict
        var svc = new AttendanceService(_db);

        // probe distance = sqrt(0.3^2 *3) ~= 0.52 > 0.3 threshold
        var probe = new List<double> { 0.3, 0.3, 0.3 };
        var match = await svc.MatchFaceAsync(probe);

        Assert.Null(match.Employee);
        Assert.True(match.Distance > 0.3);
    }

    [Fact]
    public async Task ThresholdRead_FromSettings_LooserThresholdMatches()
    {
        await SeedEmployeeAsync(new List<double> { 0.0, 0.0, 0.0 });
        SetThreshold(0.7); // loose
        var svc = new AttendanceService(_db);

        // distance ~0.52, would fail at 0.3 but passes at 0.7
        var probe = new List<double> { 0.3, 0.3, 0.3 };
        var match = await svc.MatchFaceAsync(probe);

        Assert.NotNull(match.Employee);
    }

    [Fact]
    public async Task NoSettingsRow_FallsBackToDefaultThreshold()
    {
        await SeedEmployeeAsync(new List<double> { 0.0, 0.0, 0.0 });
        // no AppSetting seeded -> default 0.5
        var svc = new AttendanceService(_db);

        Assert.Equal(0.5, await svc.GetFaceMatchThresholdAsync());

        var probe = new List<double> { 0.1, 0.1, 0.1 }; // ~0.173 < 0.5
        var match = await svc.MatchFaceAsync(probe);
        Assert.NotNull(match.Employee);
    }

    [Fact]
    public async Task MultipleDescriptors_MatchesWhenNearAnyOne()
    {
        // Two enrolled descriptors; the probe is FAR from the first but NEAR the second.
        var d1 = new List<double> { 0.0, 0.0, 0.0 };
        var d2 = new List<double> { 1.0, 1.0, 1.0 };
        await SeedEmployeeAsync(new List<List<double>> { d1, d2 });
        SetThreshold(0.5);
        var svc = new AttendanceService(_db);

        // dist to d1 ~ sqrt(0.9^2*3) ~= 1.56 (>0.5), dist to d2 ~ sqrt(0.1^2*3) ~= 0.173 (<0.5)
        var probe = new List<double> { 0.9, 0.9, 0.9 };
        var match = await svc.MatchFaceAsync(probe);

        Assert.NotNull(match.Employee);
        Assert.Equal("EMP001", match.Employee!.Code);
        // Returned distance is the MIN across descriptors (the near one), not the far one.
        Assert.True(match.Distance is > 0.17 and < 0.18, $"expected ~0.173 got {match.Distance}");
    }

    [Fact]
    public async Task MinDistanceAcrossDescriptors_IsReturned()
    {
        // Three descriptors at increasing distance from the probe; service must pick the smallest.
        var near = new List<double> { 0.05, 0.05, 0.05 };
        var mid = new List<double> { 0.3, 0.3, 0.3 };
        var far = new List<double> { 0.6, 0.6, 0.6 };
        await SeedEmployeeAsync(new List<List<double>> { far, near, mid });
        SetThreshold(0.5);
        var svc = new AttendanceService(_db);

        var probe = new List<double> { 0.0, 0.0, 0.0 };
        var match = await svc.MatchFaceAsync(probe);

        Assert.NotNull(match.Employee);
        // sqrt(0.05^2*3) ~= 0.0866 — the MIN, proving min-across-descriptors.
        Assert.True(match.Distance is > 0.08 and < 0.09, $"expected ~0.0866 got {match.Distance}");
    }

    [Fact]
    public async Task BeyondThreshold_ForAllDescriptors_NoMatch()
    {
        var d1 = new List<double> { 0.0, 0.0, 0.0 };
        var d2 = new List<double> { 0.5, 0.5, 0.5 };
        await SeedEmployeeAsync(new List<List<double>> { d1, d2 });
        SetThreshold(0.3); // strict
        var svc = new AttendanceService(_db);

        // dist to d1 ~0.52, dist to d2 ~0.69 — both > 0.3
        var probe = new List<double> { 0.3, 0.3, 0.3 };
        var match = await svc.MatchFaceAsync(probe);

        Assert.Null(match.Employee);
        Assert.True(match.Distance > 0.3);
    }
}
