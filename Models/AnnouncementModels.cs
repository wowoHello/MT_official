namespace MT.Models;

// ─── 公告分類 (DB: MT_Announcements.Category TINYINT) ───
public enum AnnouncementCategory : byte
{
    System = 1,   // 系統公告（藍）
    Compose = 2,  // 命題公告（綠）
    Review = 3,   // 審題公告（紫）
    Other = 4     // 其它（灰）
}

// ─── 公告狀態 (DB: MT_Announcements.Status TINYINT) ───
// DB 僅存兩種：0=草稿、1=發佈
// 「已下架」由前端根據日期推導（發佈 + 不在上架~下架期間）
public enum AnnouncementStatus : byte
{
    Draft = 0,     // 草稿
    Published = 1  // 發佈
}

// ─── 梯次生命週期狀態（給 Combobox 分組用） ───
// 由 GetProjectDropdownAsync SQL 端推導：
//   ClosedAt 非 NULL → Closed
//   命題階段 StartDate <= 今天 → Active
//   否則 → Preparing
public enum ProjectLifecycleGroup : byte
{
    Preparing = 1, // 準備中
    Active    = 2, // 進行中
    Closed    = 3  // 已結案
}

// ─── 列表項目 DTO ───
// 多選後：ProjectIds 空 list = 全站廣播；非空 = 指定的梯次集合
public class AnnouncementListItem
{
    public int Id { get; set; }
    public byte Category { get; set; }
    public byte Status { get; set; }

    // 從 STUFF + FOR XML 拼出的 csv（"1,3,5"）；C# 端 split 後填 ProjectIds
    public string? ProjectIdsCsv { get; set; }
    public string? ProjectNamesCsv { get; set; }

    // 解析後的陣列（從 csv 派生）
    public IReadOnlyList<int> ProjectIds
        => string.IsNullOrEmpty(ProjectIdsCsv)
            ? []
            : ProjectIdsCsv.Split(',').Select(int.Parse).ToArray();

    public IReadOnlyList<string> ProjectNames
        => string.IsNullOrEmpty(ProjectNamesCsv)
            ? []
            : ProjectNamesCsv.Split(',');

    public DateTime PublishDate { get; set; }
    public DateTime? UnpublishDate { get; set; }
    public bool IsPinned { get; set; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 列表「綁定梯次」欄顯示文字：
    /// 0 梯次 → 「全站廣播」
    /// 1 梯次 → 梯次名稱
    /// N (N&gt;1) 梯次 → 「首梯次 +N-1」
    /// </summary>
    public string ProjectBindingDisplay
    {
        get
        {
            var names = ProjectNames;
            if (names.Count == 0) return "全站廣播";
            if (names.Count == 1) return names[0];
            return $"{names[0]} +{names.Count - 1}";
        }
    }

    /// <summary>hover 用完整列表（多梯次以「、」串接，全站廣播回空）</summary>
    public string ProjectBindingTooltip
        => ProjectNames.Count == 0 ? "" : string.Join("、", ProjectNames);

    /// <summary>
    /// 取得指定梯次 Id 對應的梯次名稱（Home 詳情面板用，只顯示當前切到的梯次 chip）。
    /// 找不到回 null；當前無切到梯次傳入 null 也回 null。
    /// 仰賴 ProjectIds 與 ProjectNames 同序 1:1 對應（SQL STUFF + FOR XML 兩段都 ORDER BY ap.Id）。
    /// </summary>
    public string? GetProjectNameByCurrentId(int? currentProjectId)
    {
        if (currentProjectId is null) return null;
        var ids = ProjectIds;
        var names = ProjectNames;
        for (int i = 0; i < ids.Count && i < names.Count; i++)
            if (ids[i] == currentProjectId.Value) return names[i];
        return null;
    }

    /// <summary>
    /// 根據 DB Status + 日期推導顯示狀態：
    /// Status=0 → 草稿
    /// Status=1 + 今天 &lt; 上架日 → 未發佈
    /// Status=1 + 在上架~下架期間 → 已發佈
    /// Status=1 + 今天 &gt; 下架日 → 已下架
    /// </summary>
    public string DisplayStatus
    {
        get
        {
            if (Status == 0) return "草稿";
            var now = DateTime.Now;
            if (now < PublishDate) return "未發佈";
            if (UnpublishDate.HasValue && now > UnpublishDate.Value) return "已下架";
            return "已發佈";
        }
    }
}

// ─── 統計卡片 DTO（前端計算，不再由 SQL 統計） ───
public class AnnouncementStats
{
    public int Total { get; set; }
    public int Published { get; set; }
    public int Draft { get; set; }
    public int Archived { get; set; }
    public int Pinned { get; set; }
}

// ─── 編輯載入 DTO ───
// ProjectIds 從 MT_AnnouncementProjects junction 表 GROUP_CONCAT 後 C# 端 split
public class AnnouncementEditDto
{
    public int Id { get; set; }
    public byte Category { get; set; }
    public byte Status { get; set; }
    public DateTime PublishDate { get; set; }
    public DateTime? UnpublishDate { get; set; }
    public bool IsPinned { get; set; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public List<int> ProjectIds { get; set; } = [];   // 空 list = 全站廣播
}

// ─── 梯次下拉選項 DTO（給 Combobox 分組用） ───
// 加 ProjectType / Year / LifecycleStatus 三個欄位給 UI 渲染徽章、年度、分組
public class ProjectDropdownItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public byte ProjectType { get; set; }       // 0=CWT, 1=LCT；UI 端切徽章顏色
    public int Year { get; set; }
    public byte LifecycleStatus { get; set; }   // ProjectLifecycleGroup 1=Preparing, 2=Active, 3=Closed
}

// ─── 分組後的下拉資料（Combobox UI 用） ───
public class ProjectGroupedDropdown
{
    public IReadOnlyList<ProjectDropdownItem> Active    { get; init; } = [];  // 進行中
    public IReadOnlyList<ProjectDropdownItem> Preparing { get; init; } = [];  // 準備中
    public IReadOnlyList<ProjectDropdownItem> Closed    { get; init; } = [];  // 已結案
}

// ─── EditForm 表單模型 ───
// ProjectIds 空 list = 全站廣播；非空 = 指定的梯次集合（與「全站」互斥）
public class AnnouncementFormModel
{
    public byte Category { get; set; } = (byte)AnnouncementCategory.System;
    public byte Status { get; set; } = (byte)AnnouncementStatus.Draft;
    public List<int> ProjectIds { get; set; } = [];   // 空 = 全站廣播
    public DateTime? PublishDate { get; set; } = DateTime.Now;
    public DateTime? UnpublishDate { get; set; }
    public bool IsPinned { get; set; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
}
