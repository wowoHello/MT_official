namespace MT.Models;

// ======================================================================
//  使用說明手冊（以頁面為區分）— DTO 與 11 頁固定槽位目錄
//  對應頁面：Announcements（上傳管理）/ Home（預覽清單）/ Login（頁面介紹）
//  儲存：MT_UserGuideFiles（FileName=原始檔名、FilePath=guid 路徑、PageKey=頁面身分）
// ======================================================================

/// <summary>手冊可見性類別。</summary>
public enum GuideAudience
{
    /// <summary>僅登入頁專屬入口（匿名），不出現在首頁清單。</summary>
    LoginOnly,
    /// <summary>所有登入者皆可見（如首頁手冊）。</summary>
    AllUsers,
    /// <summary>依 ModuleCard 權限判定（PermissionPageUrl 對應啟用才可見）。</summary>
    Module
}

/// <summary>單一頁面手冊定義（固定 11 筆，以 Model 管理不入表）。</summary>
public sealed record GuidePageDef(
    string PageKey,            // login / home / dashboard / ... / system-logs
    string DisplayName,        // 「登入頁」「命題任務」…
    GuideAudience Audience,
    string? PermissionPageUrl  // Audience=Module 時，需對應的 ModuleCard.PageUrl
)
{
    /// <summary>下載/預覽清單顯示標題，如「命題任務使用手冊」。</summary>
    public string GuideTitle => $"{DisplayName}使用手冊";
}

/// <summary>11 頁固定手冊目錄。PageKey 對齊 ModuleCard.PageUrl。</summary>
public static class GuidePageCatalog
{
    public static readonly IReadOnlyList<GuidePageDef> All =
    [
        new("login",         "登入頁",          GuideAudience.LoginOnly, null),
        new("home",          "首頁",            GuideAudience.AllUsers,  null),
        new("dashboard",     "命題儀表板",      GuideAudience.Module,    "dashboard"),
        new("projects",      "命題專案管理",    GuideAudience.Module,    "projects"),
        new("overview",      "命題總覽",        GuideAudience.Module,    "overview"),
        new("cwt-list",      "命題任務",        GuideAudience.Module,    "cwt-list"),
        new("reviews",       "審題任務",        GuideAudience.Module,    "reviews"),
        new("teachers",      "教師管理系統",    GuideAudience.Module,    "teachers"),
        new("roles",         "角色與權限管理",  GuideAudience.Module,    "roles"),
        new("announcements", "系統公告/使用說明", GuideAudience.Module,  "announcements"),
        // 系統活動記錄不在 8 大模組內 → 併入「角色與權限管理」(roles) 權限
        new("system-logs",   "系統活動記錄",    GuideAudience.Module,    "roles"),
    ];

    public static GuidePageDef? Find(string pageKey) =>
        All.FirstOrDefault(d => d.PageKey == pageKey);

    /// <summary>PageUrl 正規化（去前導斜線、轉小寫），供權限比對用。</summary>
    public static string Normalize(string? pageUrl) =>
        (pageUrl ?? "").Trim().TrimStart('/').ToLowerInvariant();
}

/// <summary>管理端（Announcements）單一槽位現況。</summary>
public sealed class GuideSlotItem
{
    public string PageKey { get; init; } = "";
    public string PageName { get; init; } = "";
    public bool IsUploaded { get; init; }
    public string? FileName { get; init; }         // 原始檔名
    public string? FileSizeText { get; init; }     // 「2.4 MB」
    public string? UploadedDisplay { get; init; }  // 「2026/05/30 14:32」（取自實體檔 LastWriteTime）
    public string? RelativeUrl { get; init; }       // 「uploads/guides/{guid}.pdf?v={ticks}」，預覽用
}

/// <summary>下載/預覽端（Home / Login）單一可見手冊。</summary>
public sealed class GuideViewItem
{
    public string PageKey { get; init; } = "";
    public string DisplayTitle { get; init; } = ""; // 「命題任務使用手冊」
    public string RelativeUrl { get; init; } = "";  // 「uploads/guides/{guid}.pdf?v={ticks}」
}
