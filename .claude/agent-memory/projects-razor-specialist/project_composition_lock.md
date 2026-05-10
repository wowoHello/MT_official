---
name: Projects 命題階段結束鎖定機制
description: 編輯模式下命題階段結束後，題數/配額/人員指派欄位全面鎖定的條件與 UI 行為
type: project
---

## IsCompositionPhaseEnded 條件

```csharp
isEditMode && stages.Length > 1 && stages[1].End is DateTime endDate && endDate < DateTime.Today
```

即：編輯模式 + stages[1]（命題階段）結束日 < 今日。
新增模式下永遠不鎖。

## 鎖定後的 UI 行為

### 區塊 3（目標題數與人員指派）
- 區塊標題出現「已鎖定（命題階段已結束）」灰色提示
- 7 種題型 input 全部 disabled
- 人員列表容器 opacity-60 + cursor-not-allowed
- Checkbox 全部 disabled（但已有命題紀錄者本來就防呆禁止取消）
- 身份下拉全部 disabled，且隱藏「命題教師」選項（除非該 slot 原本就選了命題教師）
- 附加身份 + / × 按鈕均 disabled

### 區塊 4（人員命題數量配置）
- 區塊外框顯示「已鎖定（命題階段已結束）」
- 所有配額 input disabled
- 「平均分配」按鈕 disabled
- 下方顯示 terracotta 紅字提示：命題階段結束日期

## 額外防呆：已有命題紀錄者（不論鎖定與否）
- 取消勾選被阻擋（CheckboxKey++ 強制還原顯示）
- 移除「命題教師」身份被阻擋（RoleSlotKey++ 強制還原顯示）
- 阻擋時顯示 SweetAlert2 warning 說明題數與無法操作原因
