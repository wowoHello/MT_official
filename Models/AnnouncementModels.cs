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

// ─── 列表項目 DTO ───
public class AnnouncementListItem
{
    public int Id { get; set; }
    public byte Category { get; set; }
    public byte Status { get; set; }
    public int? ProjectId { get; set; }
    public string ProjectName { get; set; } = "";
    public DateTime PublishDate { get; set; }
    public DateTime? UnpublishDate { get; set; }
    public bool IsPinned { get; set; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public DateTime CreatedAt { get; set; }

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
public class AnnouncementEditDto
{
    public int Id { get; set; }
    public byte Category { get; set; }
    public byte Status { get; set; }
    public int? ProjectId { get; set; }
    public DateTime PublishDate { get; set; }
    public DateTime? UnpublishDate { get; set; }
    public bool IsPinned { get; set; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
}

// ─── 梯次下拉選項 DTO ───
public class ProjectDropdownItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

// ─── EditForm 表單模型 ───
public class AnnouncementFormModel
{
    public byte Category { get; set; } = (byte)AnnouncementCategory.System;
    public byte Status { get; set; } = (byte)AnnouncementStatus.Draft;
    public int? ProjectId { get; set; }
    public DateTime? PublishDate { get; set; } = DateTime.Now;
    public DateTime? UnpublishDate { get; set; }
    public bool IsPinned { get; set; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
}
