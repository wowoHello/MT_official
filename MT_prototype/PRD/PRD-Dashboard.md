# PRD: 命題儀表板（Dashboard）

> **文件版本**：v1.0
> **最後更新**：2026-03-19
> **關聯檔案**：`dashboard.html`、`js/dashboard.js`、`js/shared.js`
> **目標遷移框架**：Blazor .NET 10

---

## 1. 頁面概述

命題儀表板為系統管理員專用的資料監控頁面，提供當前梯次（或歷史梯次）的命題進度總覽、題型缺口分析、逾期警示以及操作稽核日誌。頁面資料隨頂部導覽列的梯次切換動態更新。

**可檢視此頁面的角色：** 系統管理員（ADMIN）

---

## 2. 共用功能

（以下與首頁共用，詳見 PRD-FirstPage）

- 頂部導覽列 — 梯次切換、登入者名稱（Hover 彈出個人設定與登出）、鈴鐺通知（未來項目）
- 文字大小調整工具球、留言通知（未來項目）

---

## 3. 頁面版面佈局

```
┌─────────────────────────────────────────────────────────┐
│  頂部導覽列                                              │
├─────────────────────────────────────────────────────────┤
│  麵包屑（首頁 > 命題儀表板）                              │
│  頁面標題                      [專案設定] [試題總覽]      │
├──────────┬──────────┬──────────┬──────────────────────────┤
│ 總目標題數│已納入題庫│各階段審修中│退回修題/嚴重落後        │
│  (採用)  │          │          │                          │
├──────────┴──────────┴──────────┴──────────────────────────┤
│                                                           │
│  ┌─────────────────┐  ┌──────────────────────────────┐   │
│  │ 題型缺口達成率   │  │ 依題型狀態分佈表              │   │
│  │ （圓環圖）       │  │ （堆疊長條圖）                │   │
│  │   1/3 寬度       │  │   2/3 寬度                   │   │
│  └─────────────────┘  └──────────────────────────────┘   │
│                                                           │
│  ┌────────────────────┐  ┌───────────────────────────┐   │
│  │逾期與緊急待辦(TOP5)│  │ 最新稽核歷程 (LOG)         │   │
│  │   1/2 寬度         │  │   1/2 寬度                 │   │
│  └────────────────────┘  └───────────────────────────┘   │
│                                                           │
│                    [工具球浮動於右下角]                     │
└─────────────────────────────────────────────────────────┘
```

---

## 4. 功能需求

### 4.1 麵包屑導覽

- 路徑：首頁 > 命題儀表板
- 「首頁」可點擊返回 `firstpage.html`
- 「命題儀表板」為當前頁面（不可點擊）

### 4.2 快速連結按鈕

頁面標題右側提供兩個快速操作按鈕：

| 按鈕 | 樣式 | 導向 | 說明 |
|------|------|------|------|
| 專案設定 | 白底灰框 + 齒輪圖示 | `projects.html` | 快速進入當前梯次的命題專案管理頁 |
| 試題總覽 | Morandi 藍底白字 + 清單圖示 | `overview.html` | 快速進入當前梯次的命題總覽頁 |

### 4.3 上方四張統計卡片

四張卡片橫列排列（`lg:grid-cols-4`），呈現當前梯次的核心追蹤數據。

| # | 卡片名稱 | 數值含義 | 圖示 | 顏色主題 |
|---|---------|---------|------|---------|
| 1 | 總目標題數 | 此梯次需產出的試題總數（由命題專案管理設定） | 靶心 `fa-bullseye` | Morandi 藍灰 |
| 2 | 已納入題庫（採用） | 經三審後狀態為「採用」且已結案入庫的題數 | 雙勾 `fa-check-double` | Sage 綠 |
| 3 | 各階段審修中 | 目前正在互審/專審/總審/各階段修題中的題數 | 旋轉齒輪 `fa-spinner` | 琥珀黃 |
| 4 | 退回修題/嚴重落後 | 被退回但尚未修改的題數 + 超過期限仍未繳交的題數 | 三角警告 `fa-triangle-exclamation` | Terracotta 紅橘 |

**卡片互動：**
- Hover 效果：卡片微浮（`translateY(-2px)`）+ 陰影加深
- 數字載入動畫：從 0 逐步跳增至目標值（約 800ms）
- 第 4 張卡片左側有 Terracotta 色粗邊框強調警示，圖示帶有 `animate-pulse` 閃爍效果

### 4.4 圖表區塊

使用 Chart.js 4.x 繪製，依梯次切換時動態更新資料。

#### 4.4.1 題型缺口達成率（圓環圖 Doughnut）

- 佔左側 1/3 寬度
- 圓環顯示：已達成題數（Sage 綠）vs 剩餘缺口（淺灰）
- 圓環中空比例 `cutout: 75%`（細環風格）
- 下方文字：「整體達成率：XX%」
- 計算公式：`Math.round(已達成 / 總目標 × 100)%`
- 圖例位於圖表下方

#### 4.4.2 依題型狀態分佈表（堆疊長條圖 Stacked Bar）

- 佔右側 2/3 寬度
- X 軸：各題型名稱（如：單選題、精選題、閱讀測驗、聽力測驗、短文寫作）
- Y 軸：題數
- 三層堆疊資料集：

| 圖層 | 顏色 | 說明 |
|------|------|------|
| 已結案入庫 | Sage 綠 `#8EAB94` | 最終「採用」且已入庫的題目 |
| 各階審修中 | 琥珀黃 `#f59e0b` | 正在審題或修題流程中的題目 |
| 教師草稿 | 灰色 `#d1d5db` | 仍為草稿狀態尚未送審的題目 |

- 長條圓角 `borderRadius: 4`
- Tooltip 模式：`mode: 'index', intersect: false`（hover 整列顯示所有資料集）
- 圖例位於圖表下方

**Blazor 遷移備註：**
> Chart.js 為純前端 JavaScript 圖表庫。遷移至 Blazor 時可選擇：
> 1. 透過 JS Interop 繼續使用 Chart.js（最低遷移成本）
> 2. 改用 Blazor 原生圖表元件（如 Radzen Charts、MudBlazor Charts）
> 3. 改用後端產生 SVG/圖片嵌入

### 4.5 逾期與緊急待辦（TOP 5）

- 位於底部左側 1/2 寬度
- 紅色背景標題列 + 「查看全部」連結（導向逾期完整列表）
- 顯示最多 5 筆最緊急的待辦項目
- 每筆項目包含：

| 欄位 | 說明 |
|------|------|
| 類型標籤 | 修題逾期 / 繳交逾期 / 退回未改 / 即將逾期 |
| 教師名稱 | 負責教師 |
| 任務描述 | 題目代碼或批次編號 |
| 天數 Badge | 負數為逾期（紅色）、正數為剩餘天數（橘色） |

- 無逾期項目時顯示：「目前沒有逾期或緊急的事項 🎉」
- 排序邏輯：逾期天數越多越優先（`days` 升序）

### 4.6 最新稽核歷程（LOG）

- 位於底部右側 1/2 寬度
- 時間軸（Timeline）樣式呈現
- 左側灰色直線連接各節點
- 每筆 LOG 包含：

| 欄位 | 說明 |
|------|------|
| 時間 | 發生時間（格式如 `10:45 AM`、`昨天 17:00`） |
| 動作描述 | 操作描述文字 |

- 第一筆（最新）LOG 的圓點標記為 Morandi 藍 + 外環藍光，文字加粗
- 其餘 LOG 的圓點標記為灰色
- 記錄的動作類型：

| 動作類型 | 說明 | 範例 |
|---------|------|------|
| LOGIN | 使用者登入系統 | 「陳大文 登入系統。」 |
| LOGOUT | 使用者登出系統 | 「陳大文 登出系統。」 |
| CREATE | 新增操作 | 「王小明 提交了 5 題單選題至互審區。」 |
| UPDATE | 更新操作 | 「總召(專員) 將 R-302 退回重修。」 |
| DELETE | 刪除操作 | 「管理員 刪除了草稿題目 D-045。」 |
| SYSTEM | 系統自動操作 | 「系統 自動寄發 3 封逾期提醒信件。」 |

---

## 5. 資料連動與梯次切換

### 5.1 頁面初始化流程

```
DOMContentLoaded
    ↓
shared.js: checkAuth() → initNavbar() → injectGlobalFontController()
    ↓
dashboard.js: initCharts()           → 初始化空白 Chart.js 圖表實例
    ↓
dashboard.js: loadDashboardData(projectId)
    ├→ animateValue() × 4            → 更新四張統計卡片數字
    ├→ updateDoughnutChart()          → 更新圓環圖資料
    ├→ updateBarChart()               → 更新堆疊長條圖資料
    ├→ renderUrgentsList()            → 渲染逾期待辦列表
    └→ renderLogsList()               → 渲染稽核歷程
```

### 5.2 梯次切換連動

```
projectChanged 事件
    ↓
loadDashboardData(newProjectId) → 全頁面資料重新載入與圖表重繪
```

- 切換梯次時，所有區塊（統計卡片、圖表、列表）均依新梯次資料重新渲染
- 已結案梯次同樣可查看歷史資料

### 5.3 權限控管

- 僅 `ADMIN` 角色可進入此頁面
- 非 ADMIN 角色嘗試進入時：顯示「權限不足」提示 → 2 秒後導回首頁

---

## 6. 使用情境

### 情境一：系統管理員查看儀表板並快速跳轉

> 假設目前進行中的產學合作計畫為：115年度春季全民中檢

1. 管理員從首頁點擊「命題儀表板」卡片進入本頁
2. 頁面自動載入「115年度春季全民中檢」的儀表板資料
3. 上方四張統計卡片顯示：總目標 1,200 題、已入庫 450 題、審修中 600 題、退回/落後 24 題
4. 圓環圖顯示整體達成率 37%，堆疊長條圖展示各題型的草稿/審修/入庫分佈
5. 逾期待辦顯示 4 筆緊急項目，稽核歷程顯示最近的操作 LOG
6. 操作路徑：

| 需求 | 操作 |
|------|------|
| 想查看該梯次的專案設定與指派人員 | 點擊右上**專案設定**按鈕 → 直接進入命題專案管理頁 |
| 想專注觀看所有試題進度 | 點擊右上**試題總覽**按鈕 → 進入當前梯次的命題總覽頁 |
| 想看歷史梯次的命題狀況 | 切換**頂部導覽列梯次切換器** → 選擇其他梯次 → 全頁面資料自動切換 |

---

## 7. 資料庫資料表規劃

> 本頁面主要為「查詢聚合」頁面，不產生新的資料寫入。以下為此頁面所需查詢的資料表來源與新增的彙總檢視表。

### 7.1 依賴的既有資料表（來自其他 PRD）

| 資料表 | 來源 PRD | 用途 |
|--------|---------|------|
| Projects | PRD-FirstPage | 取得梯次基本資訊與狀態 |
| ProjectPhases | PRD-FirstPage | 取得各階段起迄日期，計算倒數與逾期 |
| ProjectMembers | PRD-FirstPage | 取得梯次指派人員與命題配額 |
| AuditLogs | PRD-FirstPage | 取得稽核歷程 LOG |
| Users | PRD-Login | 取得使用者名稱 |

### 7.2 Questions（試題主表）

> 此表為命題任務頁面建立的核心資料表，儀表板需讀取其彙總統計。完整結構將於 PRD-CwtList 定義，此處列出儀表板所需的關鍵欄位。

| 欄位名稱 | 資料型態 | 說明（儀表板用途） |
|----------|---------|-------------------|
| Id | UNIQUEIDENTIFIER | 試題唯一識別碼 |
| ProjectId | UNIQUEIDENTIFIER | 所屬梯次（用於篩選） |
| QuestionType | NVARCHAR(30) | 題型代碼（用於圖表 X 軸分組） |
| Status | NVARCHAR(30) | 試題狀態（用於統計卡片與圖表分層） |
| AuthorId | UNIQUEIDENTIFIER | 命題教師（用於逾期追蹤） |
| CreatedAt | DATETIME2 | 建立時間 |
| UpdatedAt | DATETIME2 | 最後更新時間 |

**試題狀態對照（儀表板統計分類）：**

| 統計分類 | 對應 Status 值 | 統計卡片 | 圖表圖層 |
|---------|---------------|---------|---------|
| 教師草稿 | `draft`、`completed` | — | 灰色層 |
| 各階段審修中 | `submitted`、`cross_review`、`cross_revision`、`expert_review`、`expert_revision`、`chief_review`、`chief_revision` | 卡片3 | 琥珀黃層 |
| 已結案入庫（採用） | `approved` | 卡片2 | 綠色層 |
| 退回修題/嚴重落後 | 狀態為修題中但已超過該階段 EndDate | 卡片4 | — |
| 不採用 | `rejected` | — | — |

### 7.3 DashboardSummaryView（儀表板彙總檢視）

> 建議建立資料庫 View 或後端 Service 層彙總查詢，避免每次載入都進行多表 JOIN + GROUP BY。

```sql
-- 概念性 View 定義
CREATE VIEW DashboardSummaryView AS
SELECT
    q.ProjectId,
    q.QuestionType,
    COUNT(*) AS TotalCount,
    SUM(CASE WHEN q.Status = 'approved' THEN 1 ELSE 0 END) AS ApprovedCount,
    SUM(CASE WHEN q.Status IN ('submitted','cross_review','cross_revision',
        'expert_review','expert_revision','chief_review','chief_revision')
        THEN 1 ELSE 0 END) AS ReviewingCount,
    SUM(CASE WHEN q.Status IN ('draft','completed') THEN 1 ELSE 0 END) AS DraftCount
FROM Questions q
GROUP BY q.ProjectId, q.QuestionType;
```

### 7.4 OverdueTasksView（逾期待辦檢視）

> 建議建立 View 或後端查詢，結合 ProjectPhases 的 EndDate 判斷逾期。

```sql
-- 概念性 View 定義
CREATE VIEW OverdueTasksView AS
SELECT
    q.Id AS QuestionId,
    q.ProjectId,
    q.QuestionType,
    q.Status,
    q.AuthorId,
    u.Name AS AuthorName,
    pp.EndDate AS PhaseEndDate,
    DATEDIFF(DAY, pp.EndDate, GETUTCDATE()) AS OverdueDays
FROM Questions q
JOIN Users u ON q.AuthorId = u.Id
JOIN ProjectPhases pp ON q.ProjectId = pp.ProjectId
    AND pp.PhaseCode = (
        -- 對應當前狀態的階段
        CASE
            WHEN q.Status IN ('draft','completed','submitted') THEN 'proposition'
            WHEN q.Status = 'cross_revision' THEN 'cross_revision'
            WHEN q.Status = 'expert_revision' THEN 'expert_revision'
            WHEN q.Status = 'chief_revision' THEN 'chief_revision'
        END
    )
WHERE pp.EndDate < GETUTCDATE()
  AND q.Status NOT IN ('approved', 'rejected');
```

---

## 8. API 端點規劃（Blazor .NET 10 遷移參考）

| 方法 | 路徑 | 說明 |
|------|------|------|
| GET | `/api/dashboard/stats?projectId={id}` | 取得四張統計卡片數據 |
| GET | `/api/dashboard/type-distribution?projectId={id}` | 取得題型缺口達成率 + 狀態分佈圖表資料 |
| GET | `/api/dashboard/overdue?projectId={id}&top={5}` | 取得逾期與緊急待辦（TOP N） |
| GET | `/api/audit-logs?projectId={id}&limit={20}` | 取得最新稽核歷程 |

---

## 9. 已知限制與改進方向

| 項目 | 現況（Demo） | 遷移後目標 |
|------|-------------|-----------|
| 統計數據 | 前端寫死假資料 per 梯次 | 後端即時彙總查詢或快取 |
| 圖表資料 | 固定陣列，手動對應 | API 回傳結構化資料，圖表自動映射 |
| 逾期計算 | 手動寫死天數 | 後端依 ProjectPhases.EndDate 動態計算 |
| 稽核 LOG | 假資料 4 筆 | 讀取 AuditLogs 表，支援分頁與篩選 |
| 權限控管 | 前端 JS 判斷（Demo 已註解） | 後端 API + Blazor AuthorizeView 雙重控管 |
| 數字動畫 | 前端 setInterval 跳增 | 可保留前端動畫，資料來源改為 API |
| 圖表庫 | Chart.js CDN 引入 | JS Interop 或改用 Blazor 原生圖表元件 |
| 「查看全部」連結 | 無實際導向 | 導向逾期待辦完整列表頁或 Modal |
| 即時更新 | 頁面載入一次性 | 可選擇 SignalR 推播即時更新或定時輪詢 |
