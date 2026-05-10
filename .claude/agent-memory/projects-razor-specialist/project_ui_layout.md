---
name: Projects 頁面真實版面結構
description: Projects.razor 實際版面：左右分割佈局，無統計卡片，右側詳情三個子區塊
type: project
---

## 版面架構（以 2026-05-08 程式碼為準）

**絕對不存在統計卡片。** 版面是「左右分割」而非「上方統計 + 下方列表」。

### 整體骨架
```
[Breadcrumb + 標題列 + 新增梯次按鈕]（flex-shrink-0，不滾動）
[左右分割區（flex-grow，lg:overflow-hidden）]
  ├─ 左側面板（lg:w-1/3 / xl:w-1/4）
  │   ├─ 篩選列（DebouncedSearchInput + 全部/進行中/已結案 三顆按鈕）
  │   └─ 可滾動列表（專案卡片行，選中時左側藍邊 + 灰底）
  └─ 右側面板（lg:w-2/3 / xl:w-3/4，bg-oatmeal）
      ├─ 空狀態（未選取時顯示 fa-folder-open 圖示）
      └─ 詳情（選取後）
          ├─ 頂部資訊卡（名稱、年度、學校、建立者 + 操作按鈕）
          ├─ 過期未結案警示橫幅（僅當 IsExpired=true 才出現）
          └─ 主內容 grid（xl:grid-cols-2）
              ├─ 時程規劃設定卡（垂直時間軸）
              └─ 右欄（題型目標數量卡 + 指派人員卡）
```

### 右側操作按鈕規則
- 非結案專案：顯示「編輯專案」+ 「結案入庫」
- 所有狀態：顯示「移除專案」（軟刪除）

### 過期未結案警示
`IsExpired` = EndDate < 今日 且 ClosedAt 仍為 null。
此時顯示 terracotta 橫幅，引導管理員點結案入庫。

**Why:** 使用者發現舊有記憶說有統計卡片，與實際版面嚴重不符（2026-05-08 確認）。
**How to apply:** 任何涉及 Projects 頁面版面的描述或改動，先以此為準。
