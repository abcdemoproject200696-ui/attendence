namespace Attendance.Domain.Entities;

/// <summary>
/// One FCM registration token per device an employee is logged in on. Used to send
/// push notifications (e.g. task assigned). A token is unique; re-registering the
/// same token just updates its owner + timestamp.
/// </summary>
public class DeviceToken
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string Token { get; set; } = string.Empty;
    public string? Platform { get; set; }
    public DateTime UpdatedAt { get; set; }
}
