---
name: Projects 頁面整體架構（三檔現況）
description: Projects.razor/ProjectService.cs/ProjectModels.cs 三檔規模、主要方法、依賴注入與技術債現況（2026-05-20 更新）
type: project
---

## DB Schema CWT/LCT 雙模式欄位（2026-05-21 三個 SQL Migration 確認部署）

| 資料表 | 新欄位 | 型別 | 預設值 | 部署狀態 | Migration 檔案 |
|--------|--------|------|--------|----------|----------------|
| MT_Projects | `ProjectType` | TINYINT NOT NULL | DEFAULT 0 | ✅ 已執行 | migrate_project_type_and_granularity.sql |
| MT_Projects | `ExamLevel` | TINYINT NULL | — | ⏳ 待執行 | migrate_project_exam_level.sql |
| MT_ProjectTargets | `Granularity` | TINYINT NOT NULL | DEFAULT 0 | ✅ 已執行 | migrate_project_type_and_granularity.sql |
| MT_MemberQuotas | `Granularity` | TINYINT NOT NULL | DEFAULT 0 | ✅ 已執行 | migrate_memberquotas_granularity.sql（補刀） |

**語意對照**：
- `ProjectType`：0=CWT（7 種題型），1=LCT（聽力中文檢定，按難度一~五）
- `ExamLevel`（CWT 專用）：0=初等、1=中等、2=中高等、3=高等、4=優等；LCT 模式一律 NULL
- `Granularity`（CWT 閱讀/短文題組）：0=母題或單題、1=子題；LCT 一律 0

**注意**：`migrate_project_exam_level.sql` 標記「部署狀態：待執行」，`MT_Projects.ExamLevel` 欄位尚未在 DB 中建立。
`ProjectService.cs` 的 SQL 已帶入 `p.ExamLevel`，一旦部署此 migration 即可正常運作。

## 三檔規模（2026-05-21 更新）

| 檔案 | 行數 | 主要職責 |
|------|------|---------|
| Components/Pages/Projects.razor | 1834 行 | UI 標記、表單狀態、配額引擎 |
| Services/ProjectService.cs | 1182 行 | 全部 DB 操作與 SignalR 廣播 |
| Models/ProjectModels.cs | 258 行 | DTO / Enum / Helper |

## CWT / LCT 雙模式實作（2026-05-21 確認完成）

所有三個決策已完整實作，`dotnet build` 通過（0 錯誤，1 個 CLI 參數警告無關程式碼）：

### 決策 1：切換 ProjectType 策略
- 新增模式：`OnProjectTypeChanged` → `questionTypes` 整份替換 + `allocationTeachers.Clear()`
- 編輯模式：`ApplyProjectEditData` 依 `project.ProjectType` 初始化，之後 dropdown disabled 不可改
- 函式：`BuildCwtQuestionTypes()` / `BuildLctQuestionTypes()` 各自獨立靜態方法

### 決策 2：EnsureCompositionPhaseLockRespected 比對鍵
比對鍵為三元組 `(UserId, QuestionTypeId, Granularity, Level)` — 確保 CWT 模式下「閱讀題組母題」「閱讀題組子題」不互蓋，LCT 模式下五個難度等級不互蓋。

### 決策 3：LCT 配額分配
`MT_MemberQuotas` 表的 `[Level] [tinyint] NULL` 欄位已存在，無需 DB migration。
`ProjectMemberQuotaDto` 已有 `Granularity (byte)` + `Level (byte?)` 欄位，與 `ProjectTargetDto` 對齊。
LCT 模式下 `BuildLctQuestionTypes()` 產生 5 項，各自帶 `QuestionTypeId=6, Granularity=0, Level=1~5`。
`BuildProjectRequest` 寫入 `ProjectMemberQuotaDto` 時帶 `Level`，SQL 同步帶 `Granularity + Level`。

### 區塊 4 雙模式渲染（「人員命題數量配置」）
Markup 中 `@for (int j = 0; j < questionTypes.Count; j++)` 迴圈驅動：
- CWT：6 欄位（一般/閱讀母/閱讀子/長文/短文母/短文子）
- LCT：5 欄位（難度一~五）
兩者共用同一段 Markup，if/else 交由 `questionTypes` 清單決定，無額外抽象層。

## ProjectService 主要方法

| 方法 | 說明 |
|------|------|
| `GetProjectListAsync` | 左側列表（排除軟刪除） |
| `GetVisibleProjectsAsync(userId)` | 專案切換器：四段 UNION 依角色決定可見梯次 |
| `GetProjectDetailAsync(projectId)` | 右側詳情，QueryMultipleAsync 4 select 合 1 round-trip |
| `GetProjectEditAsync(projectId)` | 表單回填，QueryMultipleAsync 7 select 合 1 round-trip |
| `GetTalentPoolAsync` | 人才庫清單（啟用中教師） |
| `GetProjectRoleOptionsAsync` | 外部角色選項（Category=1） |
| `CreateProjectAsync` | Serializable transaction：主檔 INSERT + ReplaceProjectChildRecords + SyncCertificates + AuditLog |
| `UpdateProjectAsync` | Serializable transaction：主檔 UPDATE + 鎖定防呆 + ReplaceProjectChildRecords + SyncCertificates + AuditLog |
| `CloseProjectAsync` | Serializable transaction：6 步驟（見結案邏輯） |
| `SoftDeleteProjectAsync` | 軟刪除 + AuditLog |
| `GetPhasesAsync` | 取 PhaseCode > 1 的 7 個子階段（DaysLeft 計算） |
| `GetCurrentPhaseAsync` | 取今日所在階段（未結案以今日、結案以 ClosedAt 為準） |

## 依賴注入

```csharp
IDatabaseService _db
ILogger<ProjectService> _logger
IHubContext<ProjectsHub> _projectsHubContext
IHttpContextAccessor _httpContextAccessor
IQuestionTypeCatalog _typeCatalog          // 第二波 #8：啟動時快取，Targets TypeName 由 C# 端補
IAppointmentService _appointmentSvc        // 聘書同步（新增/編輯後呼叫 SyncCertificatesAsync）
```

## 重要設計決策

### ProjectCode 格式
`P{民國年}{3位流水號}` 例：P114001。Serializable tx 內 UPDLOCK+HOLDLOCK 防並發重號。

### GetVisibleProjectsAsync：四段 UNION（刻意不走 IMembershipService）
- RoleCategory=0（內部）：可見全部未刪除梯次
- RoleCategory=1（外部）+ ProjectMembers：有指派的梯次
- RoleCategory=1 + Questions：有命題紀錄的梯次
- RoleCategory=1 + ReviewAssignments：有審題紀錄的梯次

Why: 邏輯太特殊（需要四種來源取聯集），不適合 IMembershipService 的「角色集合」模型。
How to apply: 第三波改造時若要優化此查詢，需另建 SQL View 而非套用 IMembershipService。

### 聘書功能（新增，2026-05-17 確認）
`MemberDetailDto.HasDownloadableCerts` 欄位：批次查 `_appointmentSvc.GetDownloadableUserIdsInProjectAsync` 後填入，供 UI 顯示下載聘書按鈕。

### EditAppointmentPeriodModal（2026-05-20 補充）
Projects.razor 內嵌聘書起迄日編輯 Modal 的狀態變數：
```csharp
bool showEditPeriodModal    // 控制 Modal 顯示
int editPeriodUserId        // 正在編輯的目標使用者
int editPeriodProjectId     // 正在編輯的目標梯次
```
回調 `HandleEditPeriodSaved()` 呼叫 `DrawPendingAppointmentsAsync(projectId)` 重新觸發聘書渲染。

`DrawPendingAppointmentsAsync(projectId)` 流程：
1. 呼叫 `AppointmentService.GetPendingDraftsByProjectAsync(projectId)`（FileName IS NULL AND IsRevoked=0）
2. 對每筆呼叫 `JS.InvokeVoidAsync("AppointmentCert.drawAndUpload", ...)`（JS Canvas 繪製並上傳）
3. 在 `CreateProjectAsync` 完成後、`UpdateProjectAsync` 完成後都會觸發此流程。

### SwalInputResult（嵌入 Projects.razor @code 區）
結案確認需輸入「確認結案」文字的 SweetAlert2 result 解析輔助類：
```csharp
sealed class SwalInputResult
{
    bool IsConfirmed, IsDenied, IsDismissed;
    string? Value;
}
```

## 技術債現況（2026-05-17）

### 第三波 #12（ReplaceProjectChildRecordsAsync Bulk Insert）—— 仍未完成
現況：foreach 逐筆 INSERT（Phases/Targets 各一次、Members+Roles+Quotas 巢狀 N 次）。
問題：成員多時（例 20 人 × 7 題型配額）會有 20+ INSERT，在 Serializable tx 內更有鎖爭用風險。
建議：TVP（Table-Valued Parameter）或多值 VALUES 批次 INSERT。
優先度：仍為待辦，但 Plan_DB_PerfReview 已標記「最複雜，最有 ROI」。

## 結案入庫邏輯（CloseProjectAsync，同一 Serializable tx）

1. MT_Projects.ClosedAt 設 GETDATE()（已結案者拋例外）
2. MT_Questions Status=9 → 12（Archived，採用）
3. MT_Questions Status ∉ {9,11,12} → 11（ClosedNotAdopted）
4. MT_SubQuestions：母題 12 + 子題 9 → 子題 12
5. MT_SubQuestions：其餘子題 → 11
6. MT_AuditLogs（ProjectId=NULL，全站活動）

Why: 結案前後子題各自獨立結案是 Plan_022 Q2 規定的邏輯。
