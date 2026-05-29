---
name: Projects 8 階段時程引擎（2026-05-29 更新）
description: PhaseCode 1-8 結構、預設天數、連動規則、DaysLeft 計算、倒數提醒設定
type: project
---

## PhaseCode 定義（資料表 MT_ProjectPhases）

| PhaseCode | 名稱 | 預設天數（從上一階段結束+1起算） | 倒數提醒 |
|-----------|------|-------------------------------|---------|
| 1 | 產學計畫區間 | 100 天（整體框架） | 無 |
| 2 | 命題階段 | 30 天 | 倒數 5 天（HomeService 產生紅色急件） |
| 3 | 交互審題 | 7 天 | 無 |
| 4 | 互審修題 | 7 天 | 倒數 5 天 |
| 5 | 專家審題 | 14 天 | 無 |
| 6 | 專審修題 | 14 天 | 倒數 5 天 |
| 7 | 總召審題 | 14 天 | 無 |
| 8 | 總召修題 | 14 天 | 倒數 5 天 |

`GetPhasesAsync` 回傳 `PhaseCode > 1`（即 2~8，共 7 個子階段，行 1146）；PhaseCode=1 是產學計畫框架，只用於 Razor 端推算起始點，不在此方法回傳。

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

## StageItem 結構（Projects.razor @code，行 2474）

```csharp
sealed class StageItem
{
    string Name;
    string Hint;
    DateTime? Start;
    DateTime? End;
    int StartKey;   // 防呆失敗時遞增，用於 @key 強制重建 input
    int EndKey;     // 同上
}
```

## GetCurrentPhaseAsync 雙軌邏輯（ProjectService.cs 行 1173）

- 未結案：`CAST(GETDATE() AS DATE) BETWEEN StartDate AND EndDate`
- 已結案：`CAST(p.ClosedAt AS DATE) BETWEEN StartDate AND EndDate`（讓審題頁仍能呈現結案所在位置）
- 若不落在任何區間，回傳 null

## DaysLeft 計算

在 DB 端計算（`GetPhasesAsync` / `GetCurrentPhaseAsync` SQL 內）：
```sql
DATEDIFF(DAY, CAST(GETDATE() AS DATE), ph.EndDate) AS DaysLeft
```
- 正數 = 還有幾天
- 0 = 今天到期
- 負數 = 已逾期

回傳結構 `ProjectPhaseInfo`（定義於 Models/ProjectModels.cs，含 PhaseCode/PhaseName/StartDate/EndDate/DaysLeft/ClosedAt）。

## 倒數提醒連動（首頁今日提醒）

PhaseCode 2、4、6、8 在 DaysLeft <= 5 時，HomeService 的 UrgentAlerts 產生紅色急件顯示於首頁今日提醒看板。Projects 頁面本身只負責時程設定，不直接產生提醒。

## ProjectLifecycleStatus 判斷邏輯（ProjectStatusHelper，Models/ProjectModels.cs 行 242）

```
ClosedAt IS NOT NULL → Closed（結案唯一來源是手動點按「結案入庫」）
CompositionStartDate（PhaseCode=2 的 StartDate）<= 今天 → Active
否則 → Preparing
```

`IsExpired` = `!closedAt.HasValue && endDate.Date < DateTime.Today`
→ 產學區間已結束但尚未手動結案：UI 端顯示「待結案」提示橫幅，引導管理員手動點按。

**注意**：`IsExpired` 不等於自動結案。只有呼叫 `CloseProjectAsync` 後 ClosedAt 才會寫入。
