# PRD: 首頁（FirstPage）

> **文件版本**：v1.0
> **最後更新**：2026-03-19
> **關聯檔案**：`firstpage.html`、`js/firstpage.js`、`js/shared.js`
> **目標遷移框架**：Blazor .NET 10

---

## 1. 頁面概述

首頁為使用者登入後的主要入口頁面，提供功能導覽卡片、今日提醒看板與公告通知三大區塊。頁面根據登入者的角色權限動態顯示可用的功能卡片，並根據頂部導覽列選擇的梯次（專案）篩選顯示相關的提醒與公告內容。

---

## 2. 共用功能（頂部導覽列 & 工具球）

### 2.1 頂部導覽列（Navbar）

由 `shared.js` 統一注入，固定於頁面頂部（`fixed top-0`），高度 64px。

#### 2.1.1 左側：品牌 Logo

- 顯示 CWT Logo 圖示 + 「CWT 命題工作平臺」文字
- 點擊可返回首頁（`firstpage.html`）
- 行動裝置上隱藏文字僅保留圖示

#### 2.1.2 中央：梯次切換器（Project Switcher）

- 顯示當前選中梯次的**年度標籤**與**專案名稱**
- 預設行為：
  - 若 localStorage 已記錄上次觀看的梯次 → 還原該梯次
  - 若無記錄 → 預設顯示最新梯次（列表第一筆）
- 點擊展開下拉選單：
  - 頂部搜尋框：可輸入關鍵字搜尋過濾專案
  - 分組顯示：「進行中（包含準備中）」與「已結案（歷史專案）」
  - 當前選中項目以藍色底色 + 勾號標示
- 切換梯次後：
  - 更新 localStorage（`cwt_current_project`）
  - 觸發 `projectChanged` 自訂事件，通知頁面內所有監聽元件重新載入對應梯次資料
  - 右下角 Toast 提示「已切換梯次：XXX」

**角色與梯次可見性規則：**

| 角色 | 可見梯次範圍 |
|------|-------------|
| 系統管理員（ADMIN） | 所有梯次 |
| 命題教師（TEACHER） | 僅被指派的任務梯次 |
| 審題教師（REVIEWER） | 僅被分配的審題梯次 |

#### 2.1.3 右側：使用者資訊

- 顯示使用者頭像（取姓名第一個字）+ 使用者名稱
- Hover 彈出下拉選單：
  - 「個人設定」（連結至個人設定頁）
  - 「登出系統」（觸發登出確認對話框）
- 登出邏輯：
  - SweetAlert2 確認對話框
  - 確認後清除 `cwt_user`（保留 `cwt_current_project` 以供下次登入還原）
  - 重導向至登入頁面（`index.html`）

#### 2.1.4 右側：鈴鐺通知（未來新增功能）

- 預計於導覽列右側加入鈴鐺圖示
- 若有新訊息會顯示紅色小圓點數字 Badge 提示
- 目前尚未實作，為未來擴充項目

### 2.2 文字大小調整工具球

（同 PRD-Login 第 4.1 節，所有頁面共用，由 `shared.js` 注入）

### 2.3 留言通知（未來更新項目）

（同 PRD-Login 第 4.2 節，目前為灰色不可點擊狀態）

---

## 3. 頁面版面佈局

```
┌─────────────────────────────────────────────────────────┐
│  頂部導覽列（Logo │ 梯次切換器 │ 鈴鐺(未來) 使用者）    │
├─────────────────────────────────────────────────────────┤
│  功能總覽儀表板標題                     [當天日期顯示]    │
├───────────────────────────────────┬─────────────────────┤
│                                   │                     │
│    功能導覽卡片區（左側 2/3~3/4）   │  今日提醒看板       │
│    ┌──────┐ ┌──────┐ ┌──────┐     │  （右側 1/3~1/4）   │
│    │ 卡片1│ │ 卡片2│ │ 卡片3│     │                     │
│    └──────┘ └──────┘ └──────┘     │  ┌─────────────┐   │
│    ┌──────┐ ┌──────┐ ┌──────┐     │  │急件/到期警示 │   │
│    │ 卡片4│ │ 卡片5│ │ 卡片6│     │  │（凍結區）    │   │
│    └──────┘ └──────┘ └──────┘     │  └─────────────┘   │
│    ┌──────┐ ┌──────┐ ┌──────┐     │  ┌─────────────┐   │
│    │ 卡片7│ │ 卡片8│ │ 卡片9│     │  │公告與通知    │   │
│    └──────┘ └──────┘ └──────┘     │  │（滾動區）    │   │
│                                   │  └─────────────┘   │
├───────────────────────────────────┴─────────────────────┤
│                    [工具球浮動於右下角]                    │
└─────────────────────────────────────────────────────────┘
```

- **左側**：功能導覽卡片區（桌面 `lg:w-2/3 xl:w-3/4`）
- **右側**：今日提醒看板（桌面 `lg:w-1/3 xl:w-1/4`，Sticky 定位隨頁面滾動固定）
- **響應式**：行動裝置時上下堆疊（卡片在上、提醒在下）
- 卡片格線：`sm:grid-cols-2 xl:grid-cols-3`

---

## 4. 功能需求

### 4.1 當天日期顯示

- 位於頁面標題右側
- 格式：`YYYY/MM/DD (星期X)`
- 動態讀取瀏覽器當前日期
- 帶有日曆圖示、白底卡片樣式

### 4.2 功能導覽卡片

共定義 **8 張功能卡片**，根據登入者角色動態渲染。

#### 4.2.1 卡片清單與角色權限對照

| # | 卡片名稱 | 說明 | 導向頁面 | ADMIN | TEACHER | REVIEWER |
|---|---------|------|---------|-------|---------|----------|
| 1 | 命題儀表板 | 監控各題型缺口與整體命題進度 | `dashboard.html` | ✓ | ✗ | ✗ |
| 2 | 命題專案管理 | 梯次設定、派發工作與階段時程管控 | `projects.html` | ✓ | ✗ | ✗ |
| 3 | 命題總覽 | 全梯次試題列表、題目內容與審題全局結果檢視 | `overview.html` | ✓ | ✗ | ✗ |
| 4 | 命題任務 | 進行試題命製、修題，並檢視本人考題狀態 | `cwt-list.html` | ✓ | ✓ | ✗ |
| 5 | 審題任務 | 互審、專審、總審工作區塊與處置辦理 | `reviews.html` | ✓ | ✓ | ✓ |
| 6 | 教師管理系統 | 命審題人員庫維護、資歷檢視與過往命題歷程 | `teachers.html` | ✓ | ✗ | ✗ |
| 7 | 角色與權限管理 | 系統帳號新增、外部人員身份指派及功能開關 | `roles.html` | ✓ | ✗ | ✗ |
| 8 | 系統公告/使用說明 | 管理內部看板公告及操作手冊下載佈局 | `announcements.html` | ✓（可讀可寫） | ✓（唯讀） | ✓（唯讀） |

#### 4.2.2 卡片顯示邏輯

**有權限的卡片：**
- 白色背景、正常顏色圖示
- Hover 效果：Morandi 藍灰色邊框 + 陰影加深 + 圖示放大 + 顯示「進入功能 →」文字
- 點擊跳轉至對應頁面
- 各卡片依序帶有 fade-in 進場動畫（每張延遲 50ms）

**無權限的卡片：**
- 灰色底色、降低透明度（`opacity-60`）、灰階濾鏡（`grayscale`）
- 右上角顯示「🔒 無權限」標籤
- 滑鼠游標為 `not-allowed`
- 點擊無反應

#### 4.2.3 各角色登入後看到的卡片組合

**系統管理員（ADMIN）：**
1. 命題儀表板
2. 命題專案管理
3. 命題總覽
4. 命題任務
5. 審題任務
6. 教師管理系統
7. 角色與權限管理
8. 系統公告/使用說明（可讀可寫）

**命題教師（TEACHER）：**
1. 命題任務（命題與修題時可編輯）
2. 審題任務（僅互審可編輯）
3. 系統公告/使用說明（唯讀）
4. 其餘卡片顯示為灰色無權限狀態

**審題教師（REVIEWER）：**
1. 審題任務（僅被分配的審題區間可編輯）
2. 系統公告/使用說明（唯讀）
3. 其餘卡片顯示為灰色無權限狀態

### 4.3 今日提醒看板（右側邊欄）

右側邊欄為 Sticky 定位，分為兩個區塊。

#### 4.3.1 凍結區：急件/到期警示

- 紅色底色區塊，置頂固定不滾動
- 顯示與當前梯次相關的急件提醒
- 每則急件包含：
  - 閃爍三角驚嘆號圖示（`animate-pulse`）
  - 提醒文字（可點擊跳轉至對應作業頁面）
- 無急件時顯示「😊 目前尚無急件。」
- 提醒類型範例：
  - 「【命題階段】距離結案倒數 3 天！您尚有 2 題未完成。」→ 連至命題作業區
  - 「【互審修題】請留意，此階段即將於今日 23:59 關閉。」→ 連至審修作業區
  - 「【專家審題】您受邀參與的梯次有 3 題待審，請撥冗處理。」→ 連至審題任務

**觸發倒數提醒的階段（與命題專案管理的時程設定連動）：**

| 階段 | 倒數觸發條件 |
|------|------------|
| 命題階段 | 距結案 ≤ 5 天 |
| 互審修題 | 距結案 ≤ 5 天 |
| 專審修題 | 距結案 ≤ 5 天 |
| 總召修題 | 距結案 ≤ 5 天 |

#### 4.3.2 滾動區：公告與通知

- 白色底色區塊，可垂直滾動（`max-h: 400px`）
- 標題列右側顯示公告總數 Badge
- 公告排序規則：
  1. **置頂公告優先**（`isTop: true`）
  2. 同權重依**發布日期降序**排列
- 公告過濾規則：
  - 顯示當前梯次專屬公告 + 全域公告（`project === 'ALL'`）
- 每則公告顯示：
  - 置頂標籤（紅色，如有）
  - 發布日期
  - 公告標題（限顯示 2 行）
  - 內容摘要（限顯示 2 行）
- 點擊公告可展開 Modal 檢視完整內容

### 4.4 公告詳情 Modal

- 點擊公告列表項目觸發開啟
- Modal 內容：
  - **標頭**：標籤列（置頂公告 / 全域公告 / 專案標籤）、公告標題、發布時間
  - **內文**：完整公告內容（純文字轉換為段落）
  - **底部**：「確認關閉」按鈕
- 開啟/關閉帶有 scale + opacity 過渡動畫（300ms）
- 關閉方式：點擊關閉按鈕、點擊背景遮罩

---

## 5. 事件監聽與資料同步

### 5.1 梯次切換連動

當使用者在頂部導覽列切換梯次時：

```
projectChanged 事件觸發
    ↓
renderReminders(newProjectId)  → 重新過濾提醒與公告
    ↓
renderMenuCards()              → 重新渲染卡片（預留未來可依梯次顯示統計 Badge）
```

### 5.2 頁面初始化流程

```
DOMContentLoaded
    ↓
shared.js: checkAuth()         → 未登入則重導向登入頁
    ↓
shared.js: initNavbar()        → 注入頂部導覽列 + 梯次切換器
    ↓
shared.js: injectGlobalFontController()  → 注入字體工具球
    ↓
firstpage.js: displayCurrentDate()       → 顯示當天日期
    ↓
firstpage.js: renderMenuCards()          → 依角色渲染功能卡片
    ↓
firstpage.js: renderReminders(projectId) → 依梯次渲染提醒看板
    ↓
firstpage.js: initNoticeModal()          → 初始化公告 Modal
```

---

## 6. 使用情境

### 情境一：系統管理員瀏覽首頁

> 假設目前進行中的產學合作計畫為：115年度春季全民中檢

1. 管理員登入後進入首頁，頂部導覽列顯示「115年度春季全民中檢」梯次
2. 右側今日提醒看板顯示該梯次的急件警示與系統公告
3. 左側顯示全部 8 張功能卡片（全部可點擊）
4. 管理員的操作路徑：

| 需求 | 操作 |
|------|------|
| 查看目標題數、命題狀況、逾期提醒、所有 CRUD LOG 與登入登出紀錄 | 點擊**命題儀表板** |
| 新增/設定命題專案 | 點擊**命題專案管理** |
| 專注查看所有命題的進度 | 點擊**命題總覽** |
| 新增/編輯教師資料 | 點擊**教師管理系統** |
| 新增/編輯人員帳號或身分權限 | 點擊**角色與權限管理** |
| 新增/編輯系統公告或上傳使用說明 | 點擊**系統公告/使用說明** |

### 情境二：命題教師瀏覽首頁

> 假設目前進行中的產學合作計畫為：115年度春季全民中檢

1. 命題教師登入後進入首頁，確認導覽列梯次為「115年度春季全民中檢」
2. 查看右側今日提醒看板：確認有無急件提醒、新公告，確認距離命題結案日期的倒數
3. 左側顯示 3 張可點擊卡片 + 5 張灰色無權限卡片
4. 命題教師的操作路徑：

| 需求 | 操作 |
|------|------|
| 查看公告或使用說明書 | 點擊**系統公告/使用說明**（唯讀） |
| 開始命題工作 | 點擊**命題任務**進入命題作業 |
| 命題階段結束後進行互審 | 點擊**審題任務**進入互審作業 |

### 情境三：審題教師瀏覽首頁

> 假設目前進行中的產學合作計畫為：115年度春季全民中檢

1. 審題教師登入後進入首頁，確認導覽列梯次為「115年度春季全民中檢」
2. 查看右側今日提醒看板：確認有無急件提醒、新公告，確認距離專審結案日期的倒數
3. 左側顯示 2 張可點擊卡片 + 6 張灰色無權限卡片
4. 審題教師的操作路徑：

| 需求 | 操作 |
|------|------|
| 查看公告或使用說明書 | 點擊**系統公告/使用說明**（唯讀） |
| 開始審題工作 | 點擊**審題任務**進入審題作業 |
| 互審修題階段結束後進行專審 | 點擊**審題任務**進入專審作業 |

---

## 7. 資料庫資料表規劃

### 7.1 Projects（命題專案/梯次表）

| 欄位名稱 | 資料型態 | 約束 | 說明 |
|----------|---------|------|------|
| Id | UNIQUEIDENTIFIER | PK, DEFAULT NEWID() | 專案唯一識別碼 |
| Code | NVARCHAR(20) | NOT NULL, UNIQUE | 專案代碼（如 `P2026-01`） |
| Name | NVARCHAR(100) | NOT NULL | 專案名稱（如「115年度 春季全民中檢」） |
| Year | NVARCHAR(10) | NOT NULL | 年度（如「115」） |
| Status | NVARCHAR(20) | NOT NULL, DEFAULT 'preparing' | 狀態：`preparing`（準備中）、`active`（進行中）、`closed`（已結案） |
| SchoolName | NVARCHAR(100) | NULL | 合作學校名稱（選填） |
| StartDate | DATE | NULL | 產學計畫起始日 |
| EndDate | DATE | NULL | 產學計畫結束日 |
| ClosedAt | DATETIME2 | NULL | 實際結案時間 |
| CreatedAt | DATETIME2 | DEFAULT GETUTCDATE() | 建立時間 |
| UpdatedAt | DATETIME2 | DEFAULT GETUTCDATE() | 最後更新時間 |

### 7.2 ProjectPhases（專案階段時程表）

| 欄位名稱 | 資料型態 | 約束 | 說明 |
|----------|---------|------|------|
| Id | UNIQUEIDENTIFIER | PK, DEFAULT NEWID() | 唯一識別碼 |
| ProjectId | UNIQUEIDENTIFIER | FK → Projects.Id, NOT NULL | 所屬專案 |
| PhaseCode | NVARCHAR(30) | NOT NULL | 階段代碼（見下表） |
| PhaseName | NVARCHAR(50) | NOT NULL | 階段名稱 |
| StartDate | DATE | NOT NULL | 階段起始日 |
| EndDate | DATE | NOT NULL | 階段結束日 |
| SortOrder | INT | NOT NULL | 排序順序 |

**階段代碼定義：**

| SortOrder | PhaseCode | PhaseName | 倒數5天提醒 |
|-----------|-----------|-----------|------------|
| 1 | `proposition` | 命題階段 | ✓ |
| 2 | `cross_review` | 交互審題 | ✗ |
| 3 | `cross_revision` | 互審修題 | ✓ |
| 4 | `expert_review` | 專家審題 | ✗ |
| 5 | `expert_revision` | 專審修題 | ✓ |
| 6 | `chief_review` | 總召審題 | ✗ |
| 7 | `chief_revision` | 總召修題 | ✓ |

### 7.3 ProjectMembers（專案成員指派表）

| 欄位名稱 | 資料型態 | 約束 | 說明 |
|----------|---------|------|------|
| Id | UNIQUEIDENTIFIER | PK, DEFAULT NEWID() | 唯一識別碼 |
| ProjectId | UNIQUEIDENTIFIER | FK → Projects.Id, NOT NULL | 所屬專案 |
| UserId | UNIQUEIDENTIFIER | FK → Users.Id, NOT NULL | 使用者 |
| AssignedRoleCode | NVARCHAR(20) | NOT NULL | 在此專案中的身分（如 `TEACHER`、`REVIEWER`、`CHIEF`） |
| QuestionQuota | INT | DEFAULT 0 | 分配的命題數量 |
| CreatedAt | DATETIME2 | DEFAULT GETUTCDATE() | 指派時間 |

### 7.4 Announcements（系統公告表）

| 欄位名稱 | 資料型態 | 約束 | 說明 |
|----------|---------|------|------|
| Id | UNIQUEIDENTIFIER | PK, DEFAULT NEWID() | 唯一識別碼 |
| Title | NVARCHAR(200) | NOT NULL | 公告標題 |
| Content | NVARCHAR(MAX) | NOT NULL | 公告內容 |
| Category | NVARCHAR(30) | NOT NULL | 分類：`system`（系統公告）、`proposition`（命題公告）、`review`（審題公告）、`other`（其他） |
| ProjectId | UNIQUEIDENTIFIER | FK → Projects.Id, NULL | 綁定梯次（NULL 表示全域公告） |
| IsTop | BIT | DEFAULT 0 | 是否置頂 |
| IsPublished | BIT | DEFAULT 1 | 是否已發布 |
| PublishedAt | DATETIME2 | NULL | 發布時間 |
| CreatedBy | UNIQUEIDENTIFIER | FK → Users.Id, NOT NULL | 建立者 |
| CreatedAt | DATETIME2 | DEFAULT GETUTCDATE() | 建立時間 |
| UpdatedAt | DATETIME2 | DEFAULT GETUTCDATE() | 最後更新時間 |

### 7.5 UrgentReminders（急件提醒表）

| 欄位名稱 | 資料型態 | 約束 | 說明 |
|----------|---------|------|------|
| Id | UNIQUEIDENTIFIER | PK, DEFAULT NEWID() | 唯一識別碼 |
| ProjectId | UNIQUEIDENTIFIER | FK → Projects.Id, NOT NULL | 所屬專案 |
| PhaseCode | NVARCHAR(30) | NOT NULL | 觸發階段 |
| TargetRoleCode | NVARCHAR(20) | NULL | 對象角色（NULL 表示全角色） |
| TargetUserId | UNIQUEIDENTIFIER | FK → Users.Id, NULL | 對象使用者（NULL 表示全員） |
| Message | NVARCHAR(500) | NOT NULL | 提醒訊息 |
| LinkUrl | NVARCHAR(200) | NULL | 點擊跳轉連結 |
| IsActive | BIT | DEFAULT 1 | 是否有效 |
| CreatedAt | DATETIME2 | DEFAULT GETUTCDATE() | 建立時間 |

> **設計備註**：急件提醒可由系統排程自動產生（根據 ProjectPhases 的 EndDate 倒數 5 天觸發），也可由管理員手動建立。

### 7.6 AuditLogs（操作日誌表）

| 欄位名稱 | 資料型態 | 約束 | 說明 |
|----------|---------|------|------|
| Id | UNIQUEIDENTIFIER | PK, DEFAULT NEWID() | 唯一識別碼 |
| UserId | UNIQUEIDENTIFIER | FK → Users.Id, NOT NULL | 操作者 |
| ProjectId | UNIQUEIDENTIFIER | FK → Projects.Id, NULL | 相關專案 |
| Action | NVARCHAR(20) | NOT NULL | 動作類型：`CREATE`、`UPDATE`、`DELETE`、`LOGIN`、`LOGOUT` |
| TargetTable | NVARCHAR(50) | NULL | 目標資料表名稱 |
| TargetId | NVARCHAR(50) | NULL | 目標記錄 ID |
| Description | NVARCHAR(500) | NULL | 操作描述 |
| OldValue | NVARCHAR(MAX) | NULL | 變更前的值（JSON） |
| NewValue | NVARCHAR(MAX) | NULL | 變更後的值（JSON） |
| IpAddress | NVARCHAR(45) | NULL | 操作者 IP |
| CreatedAt | DATETIME2 | DEFAULT GETUTCDATE() | 操作時間 |

---

## 8. API 端點規劃（Blazor .NET 10 遷移參考）

### 8.1 首頁相關

| 方法 | 路徑 | 說明 |
|------|------|------|
| GET | `/api/menu/cards` | 取得登入者可見的功能卡片清單（依權限過濾） |
| GET | `/api/reminders?projectId={id}` | 取得指定梯次的急件提醒（依角色過濾） |
| GET | `/api/announcements?projectId={id}` | 取得指定梯次的公告列表（含全域公告） |
| GET | `/api/announcements/{id}` | 取得單一公告詳情 |

### 8.2 專案切換相關

| 方法 | 路徑 | 說明 |
|------|------|------|
| GET | `/api/projects` | 取得登入者可見的梯次列表（依角色與指派過濾） |
| PUT | `/api/auth/last-project` | 更新使用者最後觀看的梯次 ID |

---

## 9. 已知限制與改進方向

| 項目 | 現況（Demo） | 遷移後目標 |
|------|-------------|-----------|
| 功能卡片權限 | 前端 `role` 字串比對 | 後端 RBAC 權限矩陣查詢 + API 回傳可見卡片 |
| 梯次可見性 | 所有角色皆可看到全部梯次 | 依 ProjectMembers 指派關係過濾 |
| 急件提醒 | 前端寫死假資料 | 後端排程依 ProjectPhases 倒數自動產生 |
| 公告資料 | 前端寫死假資料 | 資料庫 CRUD + 後端 API |
| 即時通知 | 無 | SignalR 推播即時通知（鈴鐺功能） |
| 操作日誌 | 無 | 後端 Middleware 自動記錄 AuditLogs |
| 卡片統計 Badge | 無 | 各卡片可顯示該梯次的待辦數量統計 |
