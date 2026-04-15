namespace MT.Models;

/// <summary>
/// MT_AuditLogs.Action 操作類型。
/// 對應資料表擴充屬性：0:建立, 1:修改, 2:刪除。
/// </summary>
public enum AuditAction : byte
{
    Create = 0,
    Update = 1,
    Delete = 2,
}

/// <summary>
/// MT_AuditLogs.TargetType 目標表類型。
/// 對應資料表擴充屬性：0:Users, 1:Roles, 2:Projects, 3:Questions, 4:Announcements, 5:Teachers, 6:Reviews。
/// </summary>
public enum AuditTargetType : byte
{
    Users = 0,
    Roles = 1,
    Projects = 2,
    Questions = 3,
    Announcements = 4,
    Teachers = 5,
    Reviews = 6,
}
