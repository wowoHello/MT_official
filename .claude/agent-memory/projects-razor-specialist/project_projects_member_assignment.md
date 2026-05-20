---
name: Projects 人員指派與配額分配引擎（2026-05-20 更新）
description: 人才庫來源、PersonItem/AllocationItem 結構、配額引擎、鎖定防呆、DB 寫入策略、Level 欄位、AppointmentService 業務鍵設計
type: project
---

## 人才庫（GetTalentPoolAsync）

- 來源：MT_Teachers JOIN MT_Users，篩選 Status=1（啟用中）
- 欄位：UserId / Name（DisplayName） / Identifier（TeacherCode）
- 教師管理系統頁面可雙向同步（加入梯次 = 寫入 MT_ProjectMembers）

## Razor 端兩個內部狀態物件

### PersonItem（talentPool List）
```csharp
string Id           // 前端唯一識別，格式：UserId.ToString()
int UserId
string Name
string Identifier   // TeacherCode
bool IsSelected     // 勾選後才會被寫入人員清單
List<int> SelectedRoleIds  // 有序：[主要身份, 附加身份1, ...]；0 表示未選
int CreatedQuestionCount   // 編輯模式：已命題題數，>0 禁止取消勾選
int CheckboxKey     // 防呆還原用（勾選被阻擋時遞增）
int RoleSlotKey     // 防呆還原用（身份移除被阻擋時遞增）
```

### AllocationItem（allocationTeachers List）
```csharp
string Id
int UserId
string Name
string Identifier
int[] Quotas        // 長度 = questionTypes.Count（7 種題型），index 對應 questionTypes 順序
```

**RebuildAllocationTeachers()**: 每次配額區塊渲染前重建，篩選出 IsSelected=true 且有「命題教師」RoleId 的人員；保留既有 AllocationItem 的配額數值（換人名單時不清空）。

## 身份指派規則

- 每位人員可指派「主要身份 + 多個附加身份」（slot 可 +/- 增減）
- 所有可選身份來自 GetProjectRoleOptionsAsync（Category=1，外部角色）
- 「命題教師」身份的 RoleId 會特別追蹤（GetPropositionTeacherRoleId()）

## 配額引擎（HandleAutoDistribute）

整除 + 餘數依序補給前幾位教師：
```csharp
baseQuota = target / count;
remainder = target % count;
// 前 remainder 位多 1
allocationTeachers[t].Quotas[q] = baseQuota + (t < remainder ? 1 : 0);
```

驗證：GetValidationMessage() 比對每種題型 Sum(Quotas) == Target（綠=正確，橘=不符）。

## 命題階段結束後鎖定防呆（Service 端 + Razor 端雙重）

### Razor 端（UI 鎖定）
條件：`isEditMode && stages[1].End < DateTime.Today`（stages[1] = PhaseCode=2 命題階段）
- 題數 input 全 disabled
- 人員 checkbox 全 disabled、附加身份 +/- 禁用
- 配額 input 全 disabled、平均分配按鈕 disabled

### Service 端（後端防呆）
`EnsureCompositionPhaseLockRespected`：比對 TargetCount 和 Quota 不可變更，不符拋 InvalidOperationException。
`EnsureNoNewPropositionTeacherAsync`：不可新增「命題教師」身份，但允許移除。

## DB 寫入策略（ReplaceProjectChildRecordsAsync）

**新增模式**：`shouldClearExisting=false`，直接 INSERT。
**編輯模式**：`shouldClearExisting=true`，先 DELETE 全部子記錄（MemberQuotas → ProjectMemberRoles → ProjectMembers → ProjectTargets → ProjectPhases），再 INSERT。

**技術債（第三波 #12，仍未完成）**：
目前用 foreach 逐筆 INSERT；成員多時（20人 × 7題型配額 = 最多 20+140 次 INSERT）在 Serializable tx 內有鎖爭用風險。建議改 TVP 或多值 VALUES 批次。

## MemberDetailDto.HasDownloadableCerts（新欄位，2026-05-17 確認）

GetProjectDetailAsync 取完成員清單後，批次呼叫 `_appointmentSvc.GetDownloadableUserIdsInProjectAsync(projectId)`，填入每位成員的聘書可下載標記，供右側詳情區的「下載聘書」按鈕條件渲染。

## DB Level 欄位（2026-05-20 補充）

`MT_ProjectTargets.Level TINYINT NULL` 與 `MT_MemberQuotas.Level TINYINT NULL` 兩個欄位在 db.md schema 中存在，但目前 UI 送出時一律傳 NULL，Service 端 INSERT 也不帶此欄位（DB 預設 NULL）。設計意圖是未來支援「按等級分設題目配額」，目前尚未啟用。

**How to apply**: 若未來要開啟按等級配額，需同時修改 `ProjectMemberQuotaDto`（加 Level 屬性）、AllocationItem Quotas 結構（2D 矩陣而非 1D 陣列）與 ReplaceProjectChildRecordsAsync 的 INSERT SQL。

## IAppointmentService 業務鍵設計（2026-05-20 補充）

`SyncCertificatesAsync` 與整個 AppointmentService 使用 **(UserId, ProjectId, RoleId)** 作為業務鍵，刻意不使用 `MT_ProjectMembers.Id` 作為外鍵。

**Why**: `ReplaceProjectChildRecordsAsync` 在編輯模式下會 DELETE 再 INSERT 全部子記錄，MT_ProjectMembers 的 `Id` 每次編輯都會換號，若 Appointment 掛 FK 到 ProjectMemberId 則每次編輯後聘書全部成為孤兒記錄。改用業務複合鍵可跨越 DELETE/INSERT 找回對應聘書。

**How to apply**: 修改 ReplaceProjectChildRecords 邏輯時，不得改變 MT_ProjectMembers 的刪除語意（全刪重插），否則 AppointmentService 的業務鍵查詢將出錯。
