---
name: 急件警示產生邏輯
description: HomeService.GetUrgentAlertsAsync 的完整邏輯：10 個結果集、5 種 AlertType、雙視角（個人/管理員）
type: project
---

## 觸發條件

- 必須有選定梯次（projectId）且 userId > 0
- 已結案的梯次（`ClosedAt IS NOT NULL` 或 `EndDate < 今日`）直接回傳空陣列

## 資料查詢（一次 10 個結果集）

| 結果集 | 內容 |
|--------|------|
| 1 | 當前進行中且倒數 ≤5 天的階段（PhaseCode > 1） |
| 2 | 使用者在該梯次的角色名稱集合 |
| 3 | 個人命題配額 vs 已產出數 |
| 4 | 個人修題中題目（Status IN 4,6,8）且本輪尚未送回覆 |
| 5 | 個人待審任務（ReviewStatus IN 0,1） |
| 6 | 系統角色分類（Category：0=內部，1=外部） |
| 7 | 全梯次配額目標 vs 已產出 |
| 8 | 最新逾期階段（最多一筆） |
| 9 | 管理員：全梯次待審彙整（ReviewStage 1/2/3） |
| 10 | 管理員：全梯次待修彙整（Status 4/6/8） |

## AlertType 定義

| Enum | 值 | 說明 | 顏色 |
|------|----|------|------|
| PhaseCountdown | 0 | 階段倒數（個人任務皆完成） | 橙色 |
| PersonalBacklog | 1 | 個人任務積壓 | 紅色 |
| QuotaGap | 2 | 命題配額缺口（管理員） | 依比例 |
| PhaseOverdue | 3 | 階段已逾期（管理員） | 紅色 + 跳閃 |
| AdminSummary | 4 | 全梯次任務彙整（PhaseCode 3~8，管理員） | 依天數 |

## AlertSeverity 定義

- `Warning`（0）：3~5 天 → 橙色
- `Critical`（1）：0~2 天 → 紅色

常數：`AlertThresholdDays = 5`，`CriticalThresholdDays = 2`

## 管理員視角判斷

`isAdmin = (系統角色 Category==0) OR (梯次角色包含「總召集人」)`

## 排序優先順序

逾期（0）> 配額缺口/AdminSummary（1）> 個人積壓（2）> 純倒數（3），同優先序再依 Severity 降序、DaysLeft 升序。

## 角色語意常數（IsDefault 角色名稱）

- `RoleProposer = "命題教師"`
- `RoleExpert = "審題委員"`
- `RoleConvener = "總召集人"`

## 各 PhaseCode 角色資格

| PhaseCode | 階段 | 有資格角色 |
|-----------|------|-----------|
| 2 | 命題 | 命題教師 |
| 3 | 互審 | 命題教師 |
| 4 | 互修 | 命題教師 |
| 5 | 專審 | 審題委員 |
| 6 | 專修 | 命題教師 |
| 7 | 總審 | 總召集人 |
| 8 | 總修 | 命題教師 |

**Why:** 角色語意靠資料庫 IsDefault 角色名稱比對，改名時會影響警示邏輯。
**How to apply:** 任何與角色判斷相關的修改須對照此表。
