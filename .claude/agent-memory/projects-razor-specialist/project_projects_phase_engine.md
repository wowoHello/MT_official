---
name: Projects 8 階段時程引擎（2026-05-17 更新）
description: PhaseCode 1-8 結構、預設天數、連動規則、DaysLeft 計算、倒數提醒設定
type: project
---

## PhaseCode 定義（資料表 MT_ProjectPhases）

| PhaseCode | 名稱 | 預設天數（從上一階段結束+1起算） | 倒數提醒 |
|-----------|------|-------------------------------|---------|
| 1 | 產學計畫區間 | 100 天（整體框架） | 無 |
| 2 | 命題階段 | 30 天 | 倒數 5 天 |
| 3 | 交互審題 | 7 天 | 無 |
| 4 | 互審修題 | 7 天 | 倒數 5 天 |
| 5 | 專家審題 | 14 天 | 無 |
| 6 | 專審修題 | 14 天 | 倒數 5 天 |
| 7 | 總召審題 | 14 天 | 無 |
| 8 | 總召修題 | 14 天 | 倒數 5 天 |

**注意**：`GetPhasesAsync` 回傳 `PhaseCode > 1`（即 2~8，共 7 個子階段）；PhaseCode=1 是產學計畫框架，只用於推算起始點。

## 連動引擎（Razor 端 OnStageStartChanged / OnStageEndChanged）

**觸發點 1：index 0（產學計畫）開始日變更**
- 以開始日 + 14 天作為命題階段開始日（緩衝期 14 天）
- 依序對 index 1~7 各取 DefaultDays[] 自動填入 Start+End
- 年度欄位自動帶入（民國年）

**觸發點 2：任一階段結束日（EndDate）變更**
- 下一階段的開始日自動設為該結束日 + 1 天

**防呆**：
- 開始日 > 結束日 → StartKey++ 強制還原 input 顯示值
- 結束日 < 開始日 → EndKey++ 強制還原 input 顯示值

## GetCurrentPhaseAsync 邏輯

- 未結案：CAST(GETDATE() AS DATE) BETWEEN StartDate AND EndDate
- 已結案：CAST(ClosedAt AS DATE) BETWEEN StartDate AND EndDate（讓審題頁仍能呈現結案位置）

## DaysLeft 計算

在 DB 端計算：`DATEDIFF(DAY, CAST(GETDATE() AS DATE), ph.EndDate)`
- 正數 = 還有幾天
- 0 = 今天到期
- 負數 = 已逾期

## 倒數提醒連動（首頁今日提醒）

PhaseCode 2、4、6、8 在 DaysLeft <= 5 時，HomeService 的 UrgentAlerts 會產生紅色倒數提醒顯示於首頁今日提醒看板。
Projects 頁面本身不產生提醒，只負責時程設定。
