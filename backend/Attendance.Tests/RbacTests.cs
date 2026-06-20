using Attendance.Api.Controllers;
using Attendance.Api.Dtos;
using Attendance.Api.Services;
using Attendance.Domain;
using Attendance.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Attendance.Tests;

/// <summary>
/// Exercises the RBAC login + permission resolution against a real SQLite (in-memory)
/// AppDbContext seeded via <see cref="DbSeeder"/>.
/// </summary>
public class RbacTests : IAsyncLifetime
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;

    public RbacTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
    }

    public async Task InitializeAsync() => await DbSeeder.SeedAsync(_db);

    public Task DisposeAsync()
    {
        _db.Dispose();
        _conn.Dispose();
        return Task.CompletedTask;
    }

    private AuthController Auth() => new(_db, new PermissionService(_db), new OtpService());
    private RolesController Roles() => new(_db, new PermissionService(_db));

    [Fact]
    public void PasswordHasher_IsDeterministic_AndHexSha256()
    {
        var h = PasswordHasher.Hash("admin123");
        Assert.Equal(64, h.Length); // 32 bytes -> 64 hex chars
        Assert.Equal(h, PasswordHasher.Hash("admin123"));
        Assert.NotEqual(h, PasswordHasher.Hash("wrong"));
        Assert.True(PasswordHasher.Verify("admin123", h));
        Assert.False(PasswordHasher.Verify("nope", h));
    }

    [Fact]
    public async Task Seed_CreatesTenPages()
    {
        Assert.Equal(10, await _db.Pages.CountAsync());
        var admin = await _db.Employees.FirstAsync(e => e.Code == "EMP001");
        Assert.Equal(1, admin.RoleId); // EMP001 is Admin
    }

    [Fact]
    public async Task Login_Admin_Succeeds_WithAllPages()
    {
        var result = await Auth().Login(new LoginRequestDto { Code = "EMP001", Password = "admin123" });
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<LoginResultDto>(ok.Value);

        Assert.Equal("EMP001", dto.Code);
        Assert.Equal(1, dto.RoleId);
        Assert.Equal("Admin", dto.RoleName);
        // Admin -> all 10 page keys.
        Assert.Equal(10, dto.AllowedPages.Count);
        Assert.Contains("dashboard", dto.AllowedPages);
        Assert.Contains("permissions", dto.AllowedPages);
        Assert.Contains("settings", dto.AllowedPages);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        var result = await Auth().Login(new LoginRequestDto { Code = "EMP001", Password = "badpass" });
        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task Login_UnknownCode_Returns401()
    {
        var result = await Auth().Login(new LoginRequestDto { Code = "NOPE", Password = "admin123" });
        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task AdminPermissions_ResolveToAllPages_RegardlessOfRows()
    {
        // Even after wiping Admin's rows, resolution must still return all pages.
        var adminRows = await _db.RolePagePermissions.Where(rp => rp.RoleId == 1).ToListAsync();
        _db.RolePagePermissions.RemoveRange(adminRows);
        await _db.SaveChangesAsync();

        var svc = new PermissionService(_db);
        var ids = await svc.GetAllowedPageIdsAsync(1);
        var keys = await svc.GetAllowedPageKeysAsync(1);

        Assert.Equal(10, ids.Count);
        Assert.Equal(10, keys.Count);
    }

    [Fact]
    public async Task HrPermissions_AreSeededSubset()
    {
        var result = await Roles().GetPermissions(2);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<RolePermissionsDto>(ok.Value);
        // HR: dashboard,kiosk,employees,daily,report,holidays,leaves,salary (8), no settings/permissions.
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8 }, dto.PageIds.OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task UpdatePermissions_ReplacesRows()
    {
        // Replace HR (role 2) with just dashboard + settings.
        var result = await Roles().UpdatePermissions(2, new RolePermissionsInputDto { PageIds = new() { 1, 9 } });
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<RolePermissionsDto>(ok.Value);
        Assert.Equal(new[] { 1, 9 }, dto.PageIds.OrderBy(x => x).ToArray());

        // Re-fetch confirms persistence.
        var refetch = await Roles().GetPermissions(2);
        var okGet = Assert.IsType<OkObjectResult>(refetch.Result);
        var dtoGet = Assert.IsType<RolePermissionsDto>(okGet.Value);
        Assert.Equal(new[] { 1, 9 }, dtoGet.PageIds.OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task UpdatePermissions_RejectsUnknownPageId()
    {
        var result = await Roles().UpdatePermissions(2, new RolePermissionsInputDto { PageIds = new() { 999 } });
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }
}
