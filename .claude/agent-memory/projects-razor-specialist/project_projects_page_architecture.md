---
name: Projects 頁面整體架構（三檔現況）
description: Projects.razor/ProjectService.cs/ProjectModels.cs 三檔規模、主要方法、依賴注入與現況快照（2026-05-29 更新）
type: project
---

## 三檔規模（2026-05-29 現況）

| 檔案 | 行數 | 主要職責 |
|------|------|---------|
| Components/Pages/Projects.razor | 2530 行 | UI 標記、表單狀態、配額引擎、NPOI Excel 組裝 |
| Services/ProjectService.cs | 1815 行 | 全部 DB 操作、SignalR 廣播、結案匯出 SQL 組裝 |
| Models/ProjectModels.cs | 318 行 | DTO / Enum / Helper（含匯出用 records） |

## DB Schema CWT/LCT 雙模式欄位（已全部部署）

| 資料表 | 欄位 | 型別 | 語意 |
|--------|------|------|------|
| MT_Projects | ProjectType | TINYINT NOT NULL DEFAULT 0 | 0=CWT, 1=LCT |
| MT_Projects | ExamLevel | TINYINT NULL | CWT 0~4（初/中/中高/高/優）；LCT NULL |
| MT_ProjectTargets | Granularity | TINYINT NOT NULL DEFAULT 0 | 0=母題/單題, 1=子題 |
| MT_MemberQuotas | Granularity | TINYINT NOT NULL DEFAULT 0 | 同上 |
| MT_MemberQuotas | Level | TINYINT NULL | LCT TypeId=6 為 1~5；其他 NULL |

## IProjectService 方法全覽

| 方法 | 行號（ProjectService.cs）| 說明 |
|------|--------------------------|------|
| `GetProjectListAsync` | 118 | 左側列表（排除軟刪除，按年度+Id 排序） |
| `GetProjectEditAsync` | 150 | 表單回填（QueryMultiple 7 select → 1 round-trip） |
| `GetVisibleProjectsAsync(userId)` | 159 | 切換器（四段 UNION，按角色 Category 分流） |
| `GetProjectDetailAsync(projectId)` | 233 | 右側詳情（QueryMultiple 4 select → 1 round-trip） |
| `GetTalentPoolAsync` | 340 | 人才庫（Status=1 啟用中教師） |
| `GetProjectRoleOptionsAsync` | 362 | 外部角色選項（Category=1） |
| `SoftDeleteProjectAsync` | 380 | 軟刪除 + AuditLog（ProjectId=NULL） |
| `CloseProjectAsync` | 426 | 結案 6 步驟（Serializable tx） |
| `CreateProjectAsync` | 538 | 建立（Serializable tx：主檔+子記錄+聘書+AuditLog） |
| `UpdateProjectAsync` | 644 | 更新（Serializable tx：舊快照比對+防呆+子記錄+聘書+AuditLog） |
| `GetPhasesAsync` | 1146 | 取 PhaseCode 2~8（DaysLeft 在 DB 端計算） |
| `GetCurrentPhaseAsync` | 1173 | 取今日所在階段（已結案以 ClosedAt 為準） |
| `GetClosedProjectExportDataAsync` | 1206 | 結案 Excel 所需資料（QueryMultiple 4 段 SQL） |

## 依賴注入（ProjectService.cs 行 88~113）

```csharp
IDatabaseService _db
ILogger<ProjectService> _logger
IHubContext<ProjectsHub> _projectsHubContext
IHttpContextAccessor _httpContextAccessor
IQuestionTypeCatalog _typeCatalog       // 啟動快取，補 TypeName
IAppointmentService _appointmentSvc     // 聘書同步
```

## ProjectCode 格式

`P{民國年}{3位流水號}` 例：P114001。Serializable tx 內 UPDLOCK+HOLDLOCK 防並發重號（行 547）。

## GetVisibleProjectsAsync：四段 UNION（行 163）

- RoleCategory=0（內部）：可見全部未刪除梯次
- RoleCategory=1 + ProjectMembers：有指派的梯次
- RoleCategory=1 + Questions：有命題紀錄的梯次
- RoleCategory=1 + ReviewAssignments：有審題紀錄的梯次

不走 IMembershipService，因為需要四種來源的聯集邏輯，不是角色集合模型。

## 結案入庫邏輯（CloseProjectAsync，同一 Serializable tx，行 426）

1. MT_Projects.ClosedAt 設 GETDATE()（已結案者拋例外，行 436）
2. MT_Questions Status=9 → 12（Archived，採用入庫，行 452）
3. MT_Questions Status ∉ {9,11,12} → 11（ClosedNotAdopted，行 463）
4. MT_SubQuestions：母題 12 + 子題 9 → 子題 12（採用入庫，行 481）
5. MT_SubQuestions：其餘子題 → 11（行 496）
6. MT_AuditLogs（ProjectId=NULL，全站活動，行 509）

## ReplaceProjectChildRecordsAsync 批次化（行 964，第三波 #12 已完成）

目前版本已批次化，共 5 次 round-trip（不再是 N 次）：
1. DELETE 現有子記錄（shouldClearExisting=true 時，5 段 DELETE 串成 1 SQL）
2. Phases：StringBuilder 多列 VALUES → 1 round-trip（行 999）
3. Targets：StringBuilder 多列 VALUES（TargetCount=0 的項目過濾掉）→ 1 round-trip（行 1019）
4. Members：`BulkInsertMembersAsync`（OUTPUT INSERTED.Id, INSERTED.UserId）→ 1 round-trip（行 1057）
5. Roles：`BulkInsertMemberRolesAsync`（跨成員合併）→ 1 round-trip（行 1080）
6. Quotas：`BulkInsertMemberQuotasAsync`（跨成員合併，QuotaCount=0 過濾）→ 1 round-trip（行 1110）

注意：SQL Server 單一指令參數上限 2100，典型梯次遠低於此（程式碼有此說明，行 1043）。

## 命題階段結束鎖定（UpdateProjectAsync，行 659）

條件：`compositionPhaseEnd（PhaseCode=2）< DateTime.Today`
- `EnsureCompositionPhaseLockRespected`：Targets + Quotas 全不可改，比對鍵 `(QuestionTypeId, Granularity, Level)` 三元組（行 1710）
- `EnsureNoNewPropositionTeacherAsync`：不可新增命題教師身份，但允許移除（行 1756）

## 聘書功能（IAppointmentService）

GetProjectDetailAsync 完成後批次查 `GetDownloadableUserIdsInProjectAsync`，填入 `MemberDetailDto.HasDownloadableCerts`（行 319）。
CreateProjectAsync / UpdateProjectAsync 完成後呼叫 `SyncCertificatesAsync`（行 598 / 712）。

`SyncCertificatesAsync` 使用 (UserId, ProjectId, RoleId) 複合鍵，不用 ProjectMemberId，因為 ReplaceProjectChildRecords 每次編輯都會刪掉重建，ProjectMemberId 會換號。

## 詳情頁 Targets 擴展邏輯（ExpandToFullTargetList，行 792）

詳情頁固定展示完整模板（CWT 6 項 / LCT 6 項），DB 缺項補 TargetCount=0，避免老專案顯示卡片數量不一致。

CWT 模板：(1,0,null) (3,0,null) (3,1,null) (4,0,null) (5,0,null) (5,1,null)
LCT 模板：(6,0,1~5) (7,0,null)

## Projects.razor 路由與 inject（行 6~11）

```razor
@page "/projects"
@inject IProjectService ProjectService
@inject IAppointmentService AppointmentService
@inject IJSRuntime JS
@inject NavigationManager Nav
@inject ILogger<Projects> Logger
```

## 頁面佈局（行 13~）

左右分割（lg:flex-row）：左側 1/3~1/4 為搜尋+列表、右側詳情面板。SlideOver 新增/編輯專案。
