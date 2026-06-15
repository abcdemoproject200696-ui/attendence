namespace Attendance.Domain.Entities;

public class RolePagePermission
{
    public int Id { get; set; }

    public int RoleId { get; set; }
    public Role? Role { get; set; }

    public int PageId { get; set; }
    public Page? Page { get; set; }
}
