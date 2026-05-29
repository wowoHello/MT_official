---
name: 急件警示產生邏輯
description: HomeService.GetUrgentAlertsAsync 的完整邏輯：10 個結果集、5 種 AlertType、雙視角（個人/管理員）（2026-05-29 查核）
type: project
---

## 觸發條件

- 必須有選定梯次（projectId）且 userId > 0
- 第一步先執行獨立 `closedCheckSql`：`ClosedAt IS NOT NULL OR EndDate < CAST(GETDATE() AS DATE)` → 結果 = 1 則直接回傳空陣列，不執行後續 10 結果集 SQL
- 所有結果集共用單一 `QueryMultipleAsync` 呼叫（參數：UserId, ProjectId, Threshold=5）

## 閥值常數

- `AlertThresholdDays = 5`（進入警示範圍）
- `CriticalThresholdDays = 2`（升為 Critical 紅色）

## 資料查詢（一次 10 個結果集，QueryMultipleAsync）

| # | 內容 | 備註 |
|---|------|------|
| 1 | 當前進行中且倒數 ≤5 天的階段（PhaseCode > 1，BETWEEN StartDate AND EndDate） | DaysLeft = DATEDIFF(DAY, TODAY, EndDate) |
| 2 | 使用者在該梯次的角色名稱集合（r.Name） | 未用 IMembershipService（已知技術債） |
| 3 | 個人命題配額 vs 已產出數（SUM(QuotaCount), COUNT Status>=1） | 算法不含子題/不做 cap，實際不直接使用 |
| 4 | 個人修題中題目（Status IN 4,6,8）本輪未送回覆 | 使用 `vw_QuestionRoundStartedAt` View，GROUP BY q.Status |
| 5 | 個人待審任務（ReviewStatus IN 0,1，按 ReviewStage） | GROUP BY ReviewStage |
| 6 | 系統角色分類（r.Category，透過 MT_Users JOIN MT_Roles） | 0=內部，1=外部 |
| 7 | 全梯次配額目標 vs 已產出（ProjectTargets 為主表，CROSS APPLY 算 Produced，含 CWT/LCT 分支與 Granularity，clamp 到 Target） | |
| 8 | 最新逾期階段（TOP 1，EndDate < 今日且下一階段未開始，ORDER BY PhaseCode DESC） | |
| 9 | 管理員：全梯次待審彙整（ReviewStatus IN 0,1，GROUP BY ReviewStage） | |
| 10 | 管理員：全梯次待修彙整（Status 4/6/8，本輪未回覆，GROUP BY q.Status） | 使用 `vw_QuestionRoundStartedAt`；與結果集 #4 邏輯完全相同（技術債） |

## 結果集 #7 的 CWT/LCT 分支設計（CROSS APPLY 內）

- `Granularity=0`（母題）：COUNT MT_Questions WHERE Status>=1，若 Target.Level IS NULL 不過濾 Level（CWT 模式），非 NULL 則加 Level 過濾（LCT 聽力 1~5）
- `Granularity=1`（子題）：COUNT MT_SubQuestions JOIN MT_Questions WHERE Status>=1（CWT 閱讀/短文題組）
- CASE WHEN Produced > TargetCount THEN TargetCount ELSE Produced END 做 clamp，防止超量

## 個人配額缺口的實際計算路徑

`const string sql` 的結果集 #3 算出的 `quotaRow` 並**不直接用於警示**。  
在 QueryMultiple 完成後，額外呼叫 `_questionService.GetMyQuotaProgressAsync(userId, projectId)` 取得精確缺口：

```csharp
var quotaProgress = await _questionService.GetMyQuotaProgressAsync(userId, projectId.Value);
var personalQuotaShortage = quotaProgress.Sum(q => Math.Max(0, q.Target - q.Completed));
```

再將 `personalQuotaShortage` 傳入 `BuildAlertForPhase`。

## AlertType 定義

| Enum | 值 | 說明 | UI 行為 |
|------|----|------|---------|
| PhaseCountdown | 0 | 階段倒數（個人任務皆完成，或管理員命題階段達標） | 橙色 dot，clock icon（isOverdue=false, isQuotaGap=false） |
| PersonalBacklog | 1 | 個人任務積壓 | 紅色 dot（animate-pulse） |
| QuotaGap | 2 | 命題配額缺口（管理員，命題階段，TargetTotal>0 且 ProducedTotal<TargetTotal） | chart-pie icon（amber-600） |
| PhaseOverdue | 3 | 階段已逾期（管理員，紅色跳閃） | 紅底 card（bg-red-50 border-red-200）、triangle-exclamation icon（red-600） |
| AdminSummary | 4 | 全梯次任務彙整（PhaseCode 3~8，管理員） | 依天數決定 Severity |

## AlertSeverity

- `Warning`（0）：剩 3~5 天 → 橙色（amber-500 dot）
- `Critical`（1）：剩 0~2 天 → 紅色（red-500 dot + animate-pulse）
- `isCritical = (Severity == Critical)`，`isOverdue = (AlertType == PhaseOverdue)` 在 Razor 端判斷樣式

## 管理員視角判斷

`isAdmin = (系統角色 Category == CategoryInternal = 0)  OR  (梯次角色包含「總召集人」)`

## 警示產生邏輯（每個進行中階段依序執行）

1. **逾期（僅管理員）**：`overdue != null && isAdmin` → PhaseOverdue（先加，不在 foreach 內）
2. **個人視角**（foreach phases）：`BuildAlertForPhase` — 依角色資格，有積壓 → PersonalBacklog，無積壓 → PhaseCountdown
3. **管理員視角 PhaseCode=2**（forEach 內）：有缺口 → QuotaGap；無缺口且同 PhaseCode 無 PhaseCountdown → PhaseCountdown
4. **管理員視角 PhaseCode 3~8**（forEach 內）：AddAdminSummaryAlert（與個人卡片可並存，但同 PhaseCode+AdminSummary 不重複）

## 排序優先順序

```
PhaseOverdue(0) > QuotaGap/AdminSummary(1) > PersonalBacklog(2) > PhaseCountdown(3)
，同優先序再依 Severity 降序（Critical 先）、DaysLeft 升序（越快到期越前）
```

## BuildAdminSummaryAlert 的 PhaseCode 對應

| PhaseCode | 任務說明 | 資料來源 |
|-----------|---------|---------|
| 3 | 人待給意見 | adminReviewByStage[1] |
| 4 | 題待修題 | adminEditingByStatus[4] |
| 5 | 題待審 | adminReviewByStage[2] |
| 6 | 題待修題 | adminEditingByStatus[6] |
| 7 | 題待審 | adminReviewByStage[3] |
| 8 | 題待修題 | adminEditingByStatus[8] |

## 各 PhaseCode 角色資格對照（個人視角）

| PhaseCode | 階段名 | 有資格角色 | 個人任務來源 |
|-----------|--------|-----------|-------------|
| 2 | 命題 | 命題教師 | `personalQuotaShortage`（QuestionService.GetMyQuotaProgressAsync 計算） |
| 3 | 互審 | 命題教師 | reviewByStage[1] |
| 4 | 互修 | 命題教師 | editingByStatus[4] |
| 5 | 專審 | 審題委員 | reviewByStage[2] |
| 6 | 專修 | 命題教師 | editingByStatus[6] |
| 7 | 總審 | 總召集人 | reviewByStage[3] |
| 8 | 總修 | 命題教師 | editingByStatus[8] |

## 角色語意常數（IsDefault=1 的固定角色名稱）

```
RoleProposer = "命題教師"
RoleExpert   = "審題委員"
RoleConvener = "總召集人"
```

**Why:** 靠資料庫 IsDefault 角色名稱比對，改名會靜默失效（不報錯但警示消失）。
**How to apply:** 任何角色名稱更動須同步確認這三個常數。

## 技術債

- **SQL 內部評論不一致**：`const string sql` 的標頭評論寫「8 個結果集」，但實際有 10 個（含管理員 9/10）
- 結果集 #4 與 #10 的 NOT EXISTS 子查詢邏輯完全相同（個人 vs 全梯次），應抽共用 CTE 或 View
- 結果集 #2 自行 JOIN MT_ProjectMembers/Roles，未使用 IMembershipService cache（第二波 #7 建好但未整合）
- alert 卡片點擊無跳頁行為（`warning_MODIFY.md` 規劃的 `?tab=compose/revision` 連結尚未實作到 Blazor）
- 結果集 #3 的 quotaRow 算出來但不用於個人警示計算（僅作為查詢骨架殘留）
