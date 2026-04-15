namespace MT.Models;

// ======================================================================
// 人員帳號管理（Tab A）
// ======================================================================

/// <summary>人員列表項目（左側列表用）。</summary>
public class InternalAccountItem
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Email { get; set; }
    public int RoleId { get; set; }
    public string RoleName { get; set; } = "";
    public bool IsDefaultRole { get; set; }
    public int Status { get; set; }
    public string? CompanyTitle { get; set; }
}

/// <summary>人員詳情（右側面板用）。</summary>
public class AccountDetailDto
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Email { get; set; }
    public int RoleId { get; set; }
    public string RoleName { get; set; } = "";
    public int RoleCategory { get; set; }
    public bool IsDefaultRole { get; set; }
    public int Status { get; set; }
    public string? CompanyTitle { get; set; }
    public string? Note { get; set; }
    public bool IsFirstLogin { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    /// <summary>該角色已啟用的功能模組 Badge。</summary>
    public List<RoleModuleBadge> EnabledModules { get; set; } = [];

    /// <summary>公告模組權限層級 (0:無, 1:檢視, 2:編輯)。</summary>
    public int AnnouncementPerm { get; set; }
}

/// <summary>新增人員帳號請求。</summary>
public class CreateAccountRequest
{
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Email { get; set; }
    public int RoleId { get; set; }
    public int Status { get; set; } = 1;
    public string? CompanyTitle { get; set; }
    public string? Note { get; set; }
}

/// <summary>編輯人員帳號請求（不含 Username 與密碼）。</summary>
public class UpdateAccountRequest
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = "";
    public string? Email { get; set; }
    public int RoleId { get; set; }
    public int Status { get; set; }
    public string? CompanyTitle { get; set; }
    public string? Note { get; set; }
}

// ======================================================================
// 角色權限管理（Tab B）
// ======================================================================

/// <summary>角色卡片（Tab B Grid 呈現用）。</summary>
public class RoleCardItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Category { get; set; }
    public string? Description { get; set; }
    public bool IsDefault { get; set; }
    public int UserCount { get; set; }
    public int EnabledModuleCount { get; set; }
    public List<RoleModuleBadge> EnabledModules { get; set; } = [];
}

/// <summary>角色卡片上的功能模組 Badge。</summary>
public class RoleModuleBadge
{
    public int ModuleId { get; set; }
    public string ModuleKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "";
    public string ColorClass { get; set; } = "";
    public string BgColorClass { get; set; } = "";
    public int SortOrder { get; set; }
}

/// <summary>角色詳情（Modal 編輯用）。</summary>
public class RoleDetailDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Category { get; set; }
    public string? Description { get; set; }
    public bool IsDefault { get; set; }
    public int UserCount { get; set; }
    public List<RolePermissionToggle> Permissions { get; set; } = [];
}

/// <summary>角色 Modal 的功能模組 Toggle 狀態。</summary>
public class RolePermissionToggle
{
    public int ModuleId { get; set; }
    public string ModuleKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "";
    public string? PageUrl { get; set; }
    public string? Description { get; set; }
    public string ColorClass { get; set; } = "";
    public string BgColorClass { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsEnabled { get; set; }
    public int AnnouncementPerm { get; set; }
}

/// <summary>新增角色請求。</summary>
public class CreateRoleRequest
{
    public string Name { get; set; } = "";
    public int Category { get; set; }
    public string? Description { get; set; }
    public List<RolePermissionInput> Permissions { get; set; } = [];
}

/// <summary>編輯角色請求。</summary>
public class UpdateRoleRequest
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Category { get; set; }
    public string? Description { get; set; }
    public List<RolePermissionInput> Permissions { get; set; } = [];
}

/// <summary>角色權限寫入項目。</summary>
public class RolePermissionInput
{
    public int ModuleId { get; set; }
    public bool IsEnabled { get; set; }
    public int AnnouncementPerm { get; set; }
}

// ======================================================================
// 共用查詢
// ======================================================================

/// <summary>角色下拉選單項目。</summary>
public class RoleOption
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Category { get; set; }
    public bool IsDefault { get; set; }
}

/// <summary>功能模組項目（Toggle 清單來源）。</summary>
public class ModuleItem
{
    public int Id { get; set; }
    public string ModuleKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "";
    public string? PageUrl { get; set; }
    public string? Description { get; set; }
    public string ColorClass { get; set; } = "";
    public string BgColorClass { get; set; } = "";
    public int SortOrder { get; set; }
}
