namespace MT.Models;

/// <summary>系統活動記錄頁面的 4 個 Tab（取代舊 LoginLogModels）。</summary>
public enum SystemLogTab : byte
{
    /// <summary>登入活動（資料源：MT_LoginLogs）。</summary>
    Login = 0,
    /// <summary>人員變動（MT_AuditLogs WHERE ProjectId IS NULL AND TargetType IN 0/1/5）。</summary>
    Members = 1,
    /// <summary>專案變動（MT_AuditLogs WHERE ProjectId IS NULL AND TargetType = 2）。</summary>
    Project = 2,
    /// <summary>公告變動（MT_AuditLogs WHERE ProjectId IS NULL AND TargetType = 4）。</summary>
    Announcement = 3
}

/// <summary>統一列表項：依 Tab 不同，部分欄位為 null。</summary>
public class SystemLogItem
{
    public long Id { get; set; }
    public int? UserId { get; set; }
    /// <summary>操作者顯示名（MT_Users.DisplayName；查不到時 fallback 為 Username）。</summary>
    public string OperatorName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string? IpAddress { get; set; }

    // ── Login Tab 專屬欄位 ─────────────────────────
    /// <summary>1=Login, 2=Logout（僅 Login Tab）。</summary>
    public byte? EventType { get; set; }
    public bool? IsSuccess { get; set; }
    public string? UserAgent { get; set; }
    public string? FailReason { get; set; }
    /// <summary>登入帳號字串（失敗時可能與 OperatorName 不同；僅 Login Tab）。</summary>
    public string? Username { get; set; }

    // ── Audit Tab 專屬欄位 ─────────────────────────
    /// <summary>0=Create, 1=Update, 2=Delete（僅 Audit 三 Tab）。</summary>
    public byte? Action { get; set; }
    /// <summary>0=Users / 1=Roles / 2=Projects / 4=Announcements / 5=Teachers（僅 Audit）。</summary>
    public byte? TargetType { get; set; }
    public int? TargetId { get; set; }
    /// <summary>反查目標表得來的友善名稱；目標已刪除時改由 OldValue/NewValue JSON 抽出。</summary>
    public string? TargetName { get; set; }
    /// <summary>原始 JSON（給 Service 層做 TargetName fallback 用，UI 不直接顯示）。</summary>
    public string? OldValue { get; set; }
    /// <summary>同上。</summary>
    public string? NewValue { get; set; }
}

/// <summary>查詢條件（Page 為 1-based，配合 Pagination.razor）。</summary>
public class SystemLogQuery
{
    public SystemLogTab Tab { get; set; } = SystemLogTab.Login;

    // Login Tab 用
    public byte? EventType { get; set; }
    public bool? IsSuccess { get; set; }

    // Audit Tab 用
    public byte? Action { get; set; }

    // 共用
    public string? Keyword { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    /// <summary>1-based 頁碼（對齊 Pagination 元件介面）。</summary>
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 30;
}

/// <summary>分頁結果。</summary>
public class SystemLogPage
{
    public List<SystemLogItem> Items { get; set; } = [];
    public int TotalCount { get; set; }
    /// <summary>已換算的總頁數，配合 Pagination 元件直接使用。</summary>
    public int TotalPages { get; set; }
}

/// <summary>登入活動 4 張 KPI 卡片（僅 Login Tab 顯示）。</summary>
public class SystemLogKpiDto
{
    public int TodayLoginCount { get; set; }
    public int TodayFailedCount { get; set; }
    public int WeeklyActiveUserCount { get; set; }
    public int SuspiciousLoginCount { get; set; }
}
