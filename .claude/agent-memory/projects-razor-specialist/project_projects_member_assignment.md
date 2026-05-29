---
name: Projects 人員指派與配額分配引擎（2026-05-29 更新）
description: PersonItem/AllocationItem 結構、配額引擎、鎖定防呆、DB 寫入策略（Bulk Insert 已完成）、聘書業務鍵
type: project
---

## 人才庫（GetTalentPoolAsync，行 340）

- 來源：MT_Teachers JOIN MT_Users，篩選 Status=1（啟用中）
- 欄位：UserId / Name（DisplayName） / Identifier（TeacherCode）

## Razor 端兩個內部狀態物件

### PersonItem（Projects.razor 行 2505）
```csharp
string Id           // 前端唯一識別（UserId.ToString()）
int UserId
string Name
string Identifier   // TeacherCode
bool IsSelected
List<int> SelectedRoleIds   // 有序：[主要身份, 附加身份...]；0=未選
int CreatedQuestionCount    // 編輯模式：已命題題數（>0 禁止取消勾選）
int CheckboxKey             // 防呆還原用（勾選被阻擋時遞增）
int RoleSlotKey             // 防呆還原用（身份移除被阻擋時遞增）
```

### AllocationItem（Projects.razor 行 2522）
```csharp
string Id
int UserId
string Name
string Identifier
int[] Quotas    // 長度=questionTypes.Count（CWT=6 / LCT=6），index 對應 questionTypes 順序
```

**RebuildAllocationTeachers()**（行 2408 附近）：每次渲染配額區塊前重建，篩選 IsSelected=true 且有「命題教師」RoleId 的人員；保留既有 AllocationItem 的配額數值（換人名單時不清空舊配額）。

## QuestionTypeTarget（Projects.razor 行 2485）

```csharp
sealed class QuestionTypeTarget
{
    string Key;             // 題型識別鍵（如 "general", "readingGroup" 等）
    string Label;           // 顯示標籤
    int? Count;             // 目標題數
    int QuestionTypeId;     // DB 題型 Id
    byte Granularity;       // 0=母題/整題, 1=子題
    byte? Level;            // LCT 1~5；CWT null
    string Unit;            // Key 結尾 "Group" → "組"；否則 "題"
}
```

CWT 6 欄：一般(1,0,null)、閱讀母(3,0,null)、閱讀子(3,1,null)、長文(4,0,null)、短文母(5,0,null)、短文子(5,1,null)
LCT 6 欄：難度一~五(6,0,1~5) + 聽力題組(7,0,null)

## 配額引擎（HandleAutoDistribute，行 1596 附近）

```csharp
baseQuota = target / count;
remainder = target % count;
// 前 remainder 位多 1
allocationTeachers[t].Quotas[q] = baseQuota + (t < remainder ? 1 : 0);
```

驗證（GetValidationMessage，行 1619）：比對每種題型 Sum(Quotas) == Target（綠=正確，橘=不符）。

## 命題階段結束後鎖定（雙重防呆）

### Razor 端（UI 鎖定）
條件：`isEditMode && stages[1].End < DateTime.Today`（stages[1] = PhaseCode=2 命題階段）
- 題數 input 全 disabled
- 人員 checkbox 全 disabled、附加身份 +/- 禁用
- 配額 input 全 disabled、平均分配按鈕 disabled

### Service 端（後端防呆，ProjectService.cs）
`EnsureCompositionPhaseLockRespected`（行 1710）：Targets + Quotas 不可改，比對鍵 `(QuestionTypeId, Granularity, Level)` 三元組（確保 CWT 母/子題、LCT 五個難度不互蓋）。
`EnsureNoNewPropositionTeacherAsync`（行 1756）：不可新增「命題教師」身份，但允許移除。

## DB 寫入策略（ReplaceProjectChildRecordsAsync，行 964）

**新增模式**：`shouldClearExisting=false`，直接 INSERT。
**編輯模式**：`shouldClearExisting=true`，先串聯 5 段 DELETE（MemberQuotas→ProjectMemberRoles→ProjectMembers→ProjectTargets→ProjectPhases），再批次 INSERT。

### 批次 INSERT 結構（第三波 #12，2026-05-26 已完成）

| 步驟 | 方法 | 特點 |
|------|------|------|
| Phases | 行 999，StringBuilder | 8 列單一 INSERT |
| Targets | 行 1019，StringBuilder | TargetCount=0 自動過濾 |
| Members | `BulkInsertMembersAsync`（行 1057） | OUTPUT INSERTED.Id, UserId 取回映射 |
| Roles | `BulkInsertMemberRolesAsync`（行 1080） | 跨成員合併為單一 INSERT |
| Quotas | `BulkInsertMemberQuotasAsync`（行 1110） | QuotaCount=0 過濾、跨成員合併 |

private sealed record MemberInsertRow(int Id, int UserId)（行 1140），接 OUTPUT 結果。

SQL Server 單一指令參數上限 2100，典型梯次遠低於此（行 1043 有說明）。

## IAppointmentService 業務鍵設計

`SyncCertificatesAsync` 使用 **(UserId, ProjectId, RoleId)** 複合鍵，不用 `ProjectMemberId`。

Why：`ReplaceProjectChildRecordsAsync` 編輯模式每次會全刪重建 MT_ProjectMembers，ProjectMemberId 每次編輯後都換號。若 Appointment 掛 FK 到 ProjectMemberId 則聘書成孤兒記錄。改用業務複合鍵可跨越刪/建找回對應聘書。

How to apply：修改 ReplaceProjectChildRecords 邏輯時，不得改變 MT_ProjectMembers 的全刪重建語意，否則 AppointmentService 業務鍵查詢將出錯。

## MemberDetailDto.HasDownloadableCerts（行 319）

GetProjectDetailAsync 取完成員清單後，批次查 `_appointmentSvc.GetDownloadableUserIdsInProjectAsync(projectId)` 填入每位成員的聘書可下載標記，供右側詳情區「下載聘書」按鈕條件渲染。
