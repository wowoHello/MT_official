---
name: Projects SlideOver 表單四大區塊
description: 新增/編輯專案的 SlideOver 表單實際結構與鎖定邏輯
type: project
---

## SlideOver 表單結構（CustomModal Type=SlideOver, MaxWidth=max-w-2xl）

表單使用 `<EditForm Model="formModel">` 包覆，分為四個 section：

### 區塊 1：基本設定
- 所屬年度（readonly，由產學計畫起日自動帶入民國年）
- 專案名稱（必填）
- 合作學校（選填，留空代表自辦）

### 區塊 2：時程規劃設定（8 大階段連動）
共 8 個日期行（index 0~7），各含 Start + End 兩個 date input：
| index | 名稱 | 預設天數 | 倒數提醒 |
|-------|------|---------|---------|
| 0 | 產學計畫區間 | 100天 | 無 |
| 1 | 命題階段 | 30天 | 倒數5天 |
| 2 | 交互審題 | 7天 | 無 |
| 3 | 互審修題 | 7天 | 倒數5天 |
| 4 | 專家審題 | 14天 | 無 |
| 5 | 專審修題 | 14天 | 倒數5天 |
| 6 | 總召審題 | 14天 | 無 |
| 7 | 總審修題 | 14天 | 倒數5天 |

**連動規則（OnStageStartChanged / OnStageEndChanged）：**
- index 0 開始日變更 → 自動從 +14 天推算後續 7 個子階段（每階段用 DefaultDays[]）；年度自動帶入
- 任一階段結束日變更 → 下一階段開始日自動設為 +1 天
- 防呆：開始日不可晚於結束日；結束日不可早於開始日 → 違反時 @key 遞增強制還原 input 值

### 區塊 3：目標題數與人員指派
**鎖定條件（IsCompositionPhaseEnded）：** isEditMode 且 stages[1].End < DateTime.Today

- 7 種題型目標題數（一般/精選/閱讀題組/長文/短文題組/聽力/聽力題組）
  - 題組類單位為「組」，其他為「題」
- 人員指派（來源：教師管理系統人才庫）
  - DebouncedSearchInput 搜尋姓名/編號
  - 可勾選人員 + 多個身份下拉（含附加身份 slot，可 +/- 增減）
  - 編輯模式防呆：已有命題紀錄者不可取消勾選、不可移除「命題教師」身份

### 區塊 4：人員命題數量配置
**同鎖定條件（IsCompositionPhaseEnded）**
- 自動列出「命題教師」身份的成員
- 每人 × 7 種題型的配額輸入
- 「依命題教師人數平均分配」按鈕（HandleAutoDistribute：整除 + 餘數依序補給前幾位）
- 分配狀態檢核表：顯示 已分配/需分配，綠色=正確、橘色=不符

### Footer
- 取消按鈕 + 儲存專案（新增）/ 儲存修改（編輯）按鈕
