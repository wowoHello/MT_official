---
name: Projects 狀態判斷邏輯與模型設計
description: ProjectLifecycleStatus、IsExpired、IsClosed 的計算規則與資料模型關鍵欄位
type: project
---

## ProjectLifecycleStatus 三種狀態

```
Preparing = 0  // 命題階段尚未開始
Active    = 1  // 命題階段已開始（compositionStartDate <= 今日）
Closed    = 2  // ClosedAt 有值（手動點擊「結案入庫」才寫入）
```

**完整判斷邏輯（ProjectStatusHelper.Resolve）：**
- ClosedAt 有值 → Closed（唯一結案觸發點）
- 無 ClosedAt → 以命題階段 StartDate（CompositionStartDate）為切換門檻
  - 若 CompositionStartDate 為 null → 回退為 StartDate（產學區間開始日）
  - 門檻 <= 今日 → Active；否則 → Preparing

**IsExpired（過期未結案）：**
`EndDate < 今日` 且 `ClosedAt == null`
→ UI 顯示 terracotta 警示橫幅，並非自動轉 Closed

**重要：產學區間結束日過了不等於結案！** 必須有人工點「結案入庫」。

## 關鍵欄位（ProjectListItem / ProjectDetailDto / ProjectSwitcherItem）
- `StartDate`：產學計畫開始日
- `EndDate`：產學計畫結束日（IsExpired 判斷用）
- `ClosedAt`：手動結案時間（null = 未結案）
- `CompositionStartDate`：命題階段 StartDate（來自 MT_ProjectPhases PhaseCode=2）
- `IsClosed` / `IsExpired`：計算屬性，不存 DB

## 結案入庫流程（HandleCloseProject）
1. 第一步確認：SweetAlert2 confirm 說明三大後果
2. 第二步：要求輸入「確認結案」字串防誤按
3. 呼叫 ProjectService.CloseProjectAsync(projectId, userId)
4. 重新載入列表 + 詳情

## 移除專案
- 軟刪除（SoftDeleteProjectAsync）
- 從列表隱藏，資料庫資料保留
