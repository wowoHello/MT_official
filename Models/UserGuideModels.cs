namespace MT.Models;

// ======================================================================
//  使用說明手冊（依角色區分；登入頁維持頁面制）— DTO 與槽位鍵工具
//  對應頁面：Announcements（上傳管理）/ Home（依角色預覽）/ Login（登入頁手冊）
//  儲存：MT_UserGuideFiles（FileName=原始檔名、FilePath=guid 路徑、
//        PageKey=槽位鍵："login" 或 "role:{RoleId}"）
//  說明：原本以 11 個固定頁面分槽，改為「登入頁（頁面制，匿名）+ 每個角色各一份（動態）」。
//        沿用既有 PageKey 欄位當槽位鍵，零 DB migration；唯一過濾索引天然保證一槽一份。
// ======================================================================

/// <summary>手冊槽位鍵工具：login（頁面制）與 role:{id}（角色制）兩種。</summary>
public static class GuideSlot
{
    /// <summary>登入頁槽位鍵（匿名，維持頁面制，不依角色）。</summary>
    public const string LoginKey = "login";

    /// <summary>登入頁槽位顯示名稱。</summary>
    public const string LoginDisplayName = "登入頁";

    private const string RolePrefix = "role:";

    /// <summary>組角色槽位鍵：role:{roleId}。</summary>
    public static string RoleKey(int roleId) => $"{RolePrefix}{roleId}";

    /// <summary>解析角色槽位鍵；非角色鍵（如 "login"）回 false。</summary>
    public static bool TryParseRoleKey(string? slotKey, out int roleId)
    {
        roleId = 0;
        if (string.IsNullOrEmpty(slotKey) || !slotKey.StartsWith(RolePrefix, StringComparison.Ordinal))
            return false;
        return int.TryParse(slotKey.AsSpan(RolePrefix.Length), out roleId);
    }

    /// <summary>下載/預覽清單顯示標題，如「命題教師使用手冊」。</summary>
    public static string GuideTitle(string displayName) => $"{displayName}使用手冊";
}

/// <summary>管理端（Announcements）單一槽位現況。</summary>
public sealed class GuideSlotItem
{
    /// <summary>槽位鍵："login" 或 "role:{id}"（沿用 DB 的 PageKey 欄位）。</summary>
    public string PageKey { get; init; } = "";

    /// <summary>顯示名稱：登入頁為「登入頁」，角色槽位為角色名。</summary>
    public string DisplayName { get; init; } = "";

    /// <summary>是否為登入頁槽位（與角色槽位分區顯示）。</summary>
    public bool IsLogin { get; init; }

    public bool IsUploaded { get; init; }
    public string? FileName { get; init; }         // 原始檔名
    public string? FileSizeText { get; init; }     // 「2.4 MB」
    public string? UploadedDisplay { get; init; }  // 「2026/05/30 14:32」（取自實體檔 LastWriteTime）
    public string? RelativeUrl { get; init; }       // 「uploads/guides/{guid}.pdf?v={ticks}」，預覽用
}

/// <summary>下載/預覽端（Home / Login）單一可見手冊。</summary>
public sealed class GuideViewItem
{
    public string PageKey { get; init; } = "";      // "login" 或 "role:{id}"
    public string DisplayTitle { get; init; } = ""; // 「命題教師使用手冊」
    public string RelativeUrl { get; init; } = "";  // 「uploads/guides/{guid}.pdf?v={ticks}」
}
