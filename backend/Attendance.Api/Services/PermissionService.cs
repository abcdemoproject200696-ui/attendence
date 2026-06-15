using Attendance.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Attendance.Api.Services;

/// <summary>
/// Resolves which pages a role can access. Admin (roleId 1) is ALWAYS treated as
/// having ALL pages, regardless of any RolePagePermission rows.
/// </summary>
public class PermissionService
{
    public const int AdminRoleId = 1;

    private readonly AppDbContext _db;
    public PermissionService(AppDbContext db) => _db = db;

    /// <summary>Page ids the role can access (Admin -> all page ids).</summary>
    public async Task<List<int>> GetAllowedPageIdsAsync(int roleId)
    {
        if (roleId == AdminRoleId)
            return await _db.Pages.AsNoTracking().OrderBy(p => p.MenuOrder).Select(p => p.Id).ToListAsync();

        return await _db.RolePagePermissions.AsNoTracking()
            .Where(rp => rp.RoleId == roleId)
            .Join(_db.Pages, rp => rp.PageId, p => p.Id, (rp, p) => p)
            .OrderBy(p => p.MenuOrder)
            .Select(p => p.Id)
            .ToListAsync();
    }

    /// <summary>Page keys the role can access (Admin -> all page keys).</summary>
    public async Task<List<string>> GetAllowedPageKeysAsync(int roleId)
    {
        if (roleId == AdminRoleId)
            return await _db.Pages.AsNoTracking().OrderBy(p => p.MenuOrder).Select(p => p.Key).ToListAsync();

        return await _db.RolePagePermissions.AsNoTracking()
            .Where(rp => rp.RoleId == roleId)
            .Join(_db.Pages, rp => rp.PageId, p => p.Id, (rp, p) => p)
            .OrderBy(p => p.MenuOrder)
            .Select(p => p.Key)
            .ToListAsync();
    }
}
