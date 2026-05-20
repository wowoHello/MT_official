---
name: Projects 頁面整體架構（三檔現況）
description: Projects.razor/ProjectService.cs/ProjectModels.cs 三檔規模、主要方法、依賴注入與技術債現況（2026-05-20 更新）
type: project
---

## 三檔規模（2026-05-17 量測）

| 檔案 | 行數 | 主要職責 |
|------|------|---------|
| Components/Pages/Projects.razor | ~1785 行 | UI 標記、表單狀態、配額引擎 |
| Services/ProjectService.cs | 1141 行 | 全部 DB 操作與 SignalR 廣播 |
| Models/ProjectModels.cs | 215 行 | DTO / Enum / Helper |

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
