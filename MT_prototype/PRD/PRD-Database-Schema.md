# CWT 命題工作平臺 — 跨 PRD 衝突分析 ＋ 統一資料庫規劃

> **版本**: v2.0
> **日期**: 2026-03-20
> **目的**: 彙整 10 份頁面 PRD 中的資料表定義，標示邏輯衝突，並產出一份統一的資料庫結構
> **審查依據**: db-planner 六大原則（欄位型別優化、資訊原子化、命名一致性、正規化、易讀性與高效能、註解與文件）

---

## 一、跨 PRD 邏輯衝突清單

### 🔴 衝突 1：主鍵型別不一致（UNIQUEIDENTIFIER vs INT）

| 來源 PRD | 資料表 | PK 型別 |
|----------|--------|---------|
| PRD-Login | Users, Roles, RolePermissions | UNIQUEIDENTIFIER |
| PRD-FirstPage | Projects, Announcements, etc. | UNIQUEIDENTIFIER |
| PRD-Overview | Questions, SubQuestions | UNIQUEIDENTIFIER |
| **PRD-Roles** | **Roles, RolePermissions** | **int AUTO INCREMENT** |
| **PRD-Announcements** | **Announcements, UserGuideFiles** | **int AUTO INCREMENT** |

**問題**：同一個 `Roles` 表在 PRD-Login 用 GUID，在 PRD-Roles 用 int。`Announcements` 在 PRD-FirstPage 用 GUID，在 PRD-Announcements 用 int。FK 連結必然衝突。

**✅ 統一決議**：全系統統一使用 **`int IDENTITY(1,1)`** 作為所有資料表的 PK。
理由：
- Blazor .NET 10 + EF Core 預設以 int 為主鍵效能最佳
- GUID 在索引排序、JOIN 效能上劣於 int（**欄位型別優化**原則）
- 此系統為內部平臺，不需分散式 ID 生成

---

### 🔴 衝突 2：Roles 表結構定義不同

| 欄位 | PRD-Login | PRD-Roles |
|------|-----------|-----------|
| PK 型別 | UNIQUEIDENTIFIER | int |
| `Code` | ✅ NVARCHAR(20) UNIQUE | ❌ 不存在 |
| `Category` | ❌ 不存在 | ✅ nvarchar(20) internal/external |
| `IsSystem` | ✅ BIT DEFAULT 0 | ❌ 不存在 |
| `IsDefault` | ❌ 不存在 | ✅ bit DEFAULT 0 |

**✅ 統一決議**：合併為完整版本，保留兩邊的優點：
- 保留 `Code`（用於程式邏輯判斷，如 `TEACHER`、`REVIEWER`、`CHIEF`）
- `Category` 改為 `IsInternal` BIT 欄位（**欄位型別優化**：布林語義用 bit 取代 nvarchar）
- 用 `IsDefault` 取代 `IsSystem`（語義更清楚：預設角色不可刪除、權限不可改）

---

### 🔴 衝突 3：RolePermissions 權限粒度模型不同

| 面向 | PRD-Login | PRD-Roles |
|------|-----------|-----------|
| 權限辨識 | `PermissionCode` 如 `dashboard.view` | `ModuleKey` 如 `dashboard` |
| 粒度 | 功能級（view/edit/manage） | 模組級（enabled/disabled） |
| 公告特殊欄位 | ❌ | ✅ `AnnouncementPerm` (view/edit) |

**✅ 統一決議**：以 PRD-Roles 的 **模組級開關** 為主體，因為：
- 前端 Demo 實際使用 8 模組 toggle 開關，不支援細粒度
- 公告權限用 `AnnouncementPerm` 以 `tinyint` 數字型別處理（0=view/1=edit）（**欄位型別優化**）
- 未來若需細粒度，可擴展 `PermissionLevel` 欄位

---

### 🔴 衝突 4：Announcements 表欄位名稱不同

| 欄位概念 | PRD-FirstPage | PRD-Announcements |
|----------|---------------|-------------------|
| 置頂 | `IsTop` | `IsPinned` |
| 狀態 | `IsPublished` (BIT) | `Status` (ENUM: draft/published/archived) |
| 分類 | `Category` (system/proposition/review/other) | `Category` (system/**compose**/review/other) |
| 下架日期 | ❌ 不存在 | ✅ `UnpublishDate` |
| 發佈日期 | `PublishedAt` (DATETIME2) | `PublishDate` (DATE) |
| 建立者 | `CreatedBy` | `AuthorId` |

**✅ 統一決議**：
- 置頂 → 用 `IsPinned`（語義更精準）
- 狀態 → 用 `Status` tinyint（0=草稿/1=已發佈/2=已下架），不用字串（**欄位型別優化**）
- 分類 → 用 `Category` tinyint（0=system/1=compose/2=review/3=other）（**欄位型別優化**）
- 發佈日期 → 用 `PublishDate` (DATE)，另有 `PublishedAt` (DATETIME2) 記錄實際發佈時間
- 建立者 → 用 `AuthorId`

---

### 🔴 衝突 5：試題狀態碼命名不一致

| PRD-Overview 定義 | PRD-Dashboard 使用 | 問題 |
|-------------------|--------------------|------|
| `adopted` | `approved` | 同一概念不同名稱 |
| `peer_reviewing` | `cross_review` | 命名風格不同 |
| `peer_editing` | `cross_revision` | 命名風格不同 |
| — | `submitted` | Overview 無此狀態 |

**✅ 統一決議**：以 PRD-Overview 的 14 個狀態碼為正式定義，改用 `tinyint` 數字型別（**欄位型別優化**）：
```
0=draft → 1=completed → 2=pending →
3=peer_reviewing → 4=peer_reviewed → 5=peer_editing →
6=expert_reviewing → 7=expert_reviewed → 8=expert_editing →
9=final_reviewing → 10=final_reviewed → 11=final_editing →
12=adopted / 13=rejected
```
PRD-Dashboard 須同步修正引用。

---

### 🔴 衝突 6：階段代碼命名風格不一致

| PRD-FirstPage (ProjectPhases) | PRD-Reviews (前端 key) |
|-------------------------------|------------------------|
| `proposition` | `proposing` |
| `cross_review` | `peerReview` |
| `cross_revision` | `peerEdit` |
| `expert_review` | `expertReview` |
| `expert_revision` | `expertEdit` |
| `chief_review` | `finalReview` |
| `chief_revision` | `finalEdit` |

**✅ 統一決議**：DB 使用 `tinyint`（**欄位型別優化**），程式碼中以 enum 常數對應：
```
1=proposition, 2=cross_review, 3=cross_revision,
4=expert_review, 5=expert_revision, 6=chief_review, 7=chief_revision
```
PRD-Reviews 前端 camelCase 為 UI 層命名，不影響 DB。

---

### 🟡 衝突 7：ProjectMembers 單角色 vs 多角色

| PRD-FirstPage | PRD-Projects |
|---------------|--------------|
| `ProjectMembers.AssignedRoleCode` (單一值) | 新增 `ProjectMemberRoles` 關聯表（多角色） |

**✅ 統一決議**：
- **移除** `ProjectMembers.AssignedRoleCode`
- **保留** `ProjectMemberRoles` 多角色關聯表
- 理由：PRD-Projects 明確指出同一成員可在專案中同時擔任教師與互審角色（**正規化**原則）

---

### 🟡 衝突 8：教師帳號建立入口描述不一致

| PRD-Login | PRD-Teachers |
|-----------|--------------|
| 「TEACHER / REVIEWER 帳號由管理者透過『角色與權限管理』建立」 | 教師透過「教師管理系統」建立 |

**✅ 統一決議**：
- **外部教師** → 由「教師管理系統」建立（自動生成 Users + Teachers 記錄）
- **內部人員** → 由「角色與權限管理 › 人員帳號管理」建立
- PRD-Login 的描述需修正

---

### 🟡 衝突 9：Users.RoleId FK 目標衝突

| PRD | Users.RoleId 指向 |
|-----|-------------------|
| PRD-Login | Roles.Id (UNIQUEIDENTIFIER) |
| PRD-Roles | Roles.Id (int) |

**✅ 統一決議**：統一為 `int` FK → `Roles.Id (int)`

---

### 🟢 注意事項：預設密碼不同

| 對象 | 預設密碼 | 來源 |
|------|---------|------|
| 內部人員 | `01024304`（公司統編） | PRD-Roles |
| 外部教師 | `Cwt2026!` | PRD-Teachers |

**✅ 非衝突**：這是有意設計，兩類帳號預設密碼不同是合理的。

---

## 二、db-planner 六大原則審查報告

### 原則 1：欄位型別優化

> 優先使用數字型別而非字串型別，以提高儲存效率和查詢效能。

**v1.0 問題與 v2.0 修正：**

| 資料表 | 欄位 | v1.0 型別 | v2.0 型別 | 修正理由 |
|--------|------|-----------|-----------|---------|
| Questions | Status | nvarchar(30) | tinyint | 14 個固定狀態，改用數字碼（0~13） |
| Questions | Difficulty | nvarchar(10) | tinyint | 3 個固定值（0=easy/1=medium/2=hard） |
| Questions | CurrentStage | int | tinyint | 值域 1~7，tinyint 足夠 |
| Projects | Status | nvarchar(20) | tinyint | 3 個固定值（0=preparing/1=active/2=closed） |
| Announcements | Status | nvarchar(20) | tinyint | 3 個固定值（0=draft/1=published/2=archived） |
| Announcements | Category | nvarchar(20) | tinyint | 4 個固定值（0=system/1=compose/2=review/3=other） |
| Roles | Category | nvarchar(20) | bit (IsInternal) | 二值（internal/external）→ bit |
| Users | Category | nvarchar(20) | bit (IsInternal) | 二值 → bit |
| ReviewAssignments | ReviewStage | nvarchar(10) | tinyint | 3 個固定值（1=peer/2=expert/3=final） |
| ReviewAssignments | ReviewStatus | nvarchar(10) | tinyint | 2 個固定值（0=pending/1=decided） |
| ReviewAssignments | Decision | nvarchar(10) | tinyint | 4 個固定值（0=comment/1=adopt/2=revise/3=reject） |
| RevisionReplies | ReviewStage | nvarchar(10) | tinyint | 同上 |
| SimilarityChecks | Determination | nvarchar(20) | tinyint | 3 個固定值（0=無疑慮/1=低度相似/2=高度相似） |
| AuditLogs | Action | nvarchar(20) | tinyint | 6 個固定值（0~5） |
| RolePermissions | AnnouncementPerm | nvarchar(10) | tinyint | 2 個固定值（0=view/1=edit） |
| ProjectPhases | PhaseCode | nvarchar(30) | tinyint | 7 個固定值（1~7），由 SortOrder 直接代表 |
| ProjectMemberRoles | RoleCode | nvarchar(20) | tinyint | 5 個固定值（1~5） |
| Teachers | Gender | nvarchar(5) | tinyint | 2 個固定值（0=男/1=女） |
| Teachers | Education | nvarchar(10) | tinyint | 3 個固定值（0=學士/1=碩士/2=博士） |

> **注意**：所有數字代碼的含義以 SQL 註解方式記錄於 CREATE TABLE 語句中，並於本文件之「附錄：狀態碼對照表」統一整理。

### 原則 2：資訊原子化

**v1.0 問題與 v2.0 修正：**

| 問題 | 修正方式 |
|------|---------|
| `Questions.Options` 以 JSON 儲存選項陣列 | ✅ **保留 JSON**：選項與母題為強耦合一對一，且前端直接消費 JSON，拆表反而增加 JOIN 複雜度。以 `nvarchar(MAX)` 儲存 JSON 是合理的反正規化決策 |
| `QuestionAttributes` 將多題型屬性合併為寬表 | ✅ **保留寬表**：各題型屬性互斥且僅用於 1:1 關聯，拆為多表反而增加管理複雜度 |
| `Teachers.Expertise` 以字串儲存多個專長（逗號分隔） | ✅ **保留原設計**：專長領域為自由文字描述，非結構化篩選需求，逗號分隔可接受 |

### 原則 3：命名一致性

**v1.0 問題與 v2.0 修正：**

> 本專案採用 **PascalCase** 命名慣例（與 Blazor .NET 10 + EF Core 的 C# Convention 一致）。
> db-planner 建議 snake_case，但考量到 EF Core 預設將 C# Property 名稱映射為欄位名，
> 統一使用 PascalCase 可避免命名轉換設定，降低維護成本。

| 修正項目 | 說明 |
|---------|------|
| FK 命名慣例統一 | `{目標表名單數}Id`，如 `ProjectId`、`UserId`、`AuthorId` |
| Boolean 命名加上 `Is` / `Has` 前綴 | `IsActive`、`IsDeleted`、`IsPinned`、`IsFirstLogin`、`HasSubQuestions` ✅ 已遵循 |
| 時間戳統一 | `CreatedAt`、`UpdatedAt`、`DeletedAt` ✅ 已遵循 |
| Index 命名 | `IX_{表名}_{欄位名}`，如 `IX_Questions_ProjectId` ✅ 已遵循 |
| UNIQUE 約束命名 | `UQ_{表名}` 或 `UQ_{表名}_{欄位}`，如 `UQ_ProjectPhases` ✅ 已遵循 |
| FK 約束命名 | `FK_{來源表}_{目標表}`，如 `FK_Users_Roles` ✅ 已遵循 |
| `ProjectPhases` 合併 `PhaseCode` 與 `SortOrder` | 改為僅用 `SortOrder` (tinyint) 作為階段識別碼，移除 `PhaseCode` 字串欄位（**命名一致性 + 欄位型別優化**）。階段名稱改由 `PhaseName` 保留 |

### 原則 4：正規化

**v1.0 問題與 v2.0 修正：**

| 問題 | 嚴重度 | 修正 |
|------|--------|------|
| `Questions.ReturnCount` 與 `ReviewReturnCounts.ReturnCount` 重複儲存退回次數 | 中 | ✅ **移除** `Questions.ReturnCount`，統一由 `ReviewReturnCounts` 管理。查詢時 JOIN 取得 |
| `ReviewAssignments.ProjectId` 可由 `Questions.ProjectId` 推導 | 低 | ✅ **保留**：為查詢效能的有意反正規化（避免每次都 JOIN Questions） |
| `ProjectMemberRoles.RoleCode` 為字串，未建立 FK | 中 | ✅ 改為 `tinyint`，對應程式碼 enum 常數。因專案角色代碼與系統角色不完全相同（如 CROSS_REVIEWER 不在 Roles 表），不建立 FK |
| `Users` 表的 `RememberToken` / `RememberExpiry` 與 `PasswordResetTokens` 設計模式不一致 | 低 | ✅ **保留**：RememberToken 為長效 Session Token，與短效密碼重設 Token 用途不同 |

### 原則 5：易讀性與高效能

**v1.0 問題與 v2.0 修正：**

| 修正項目 | 說明 |
|---------|------|
| 補充缺失的 Index | `IX_Questions_QuestionTypeCode`、`IX_ReviewAssignments_QuestionId`、`IX_ReviewAssignments_ReviewerId`、`IX_RevisionReplies_QuestionId`、`IX_Teachers_UserId` |
| `Questions.QuestionCode` 已有 UNIQUE 約束 | 自動建立唯一索引，無需額外索引 ✅ |
| `ProjectPhases` 覆蓋索引 | 新增 `IX_ProjectPhases_ProjectId_SortOrder` INCLUDE (StartDate, EndDate) 用於時程查詢 |
| `Announcements` 覆蓋索引 | 現有 `IX_Announcements_Status` 改為 `(Status, IsPinned DESC, PublishDate DESC)` 支援列表排序 |
| `LoginLogs` / `AuditLogs` 高成長表 | 建議後期加入分區策略（按 CreatedAt 月份分區）或定期歸檔 |
| `PasswordResetTokens` 加索引 | 新增 `IX_PRT_Token` 支援 Token 驗證查詢 |

### 原則 6：註解與文件

**v2.0 改進**：
- 所有 CREATE TABLE 語句中每個欄位皆附上 `--` 行內註解
- 數字型別狀態碼以註解標示對照值
- 新增「附錄：狀態碼對照表」統一整理所有 tinyint 的數字→含義映射
- 每張表的 SQL 前方加上區塊註解說明用途

---

## 三、統一資料庫結構規劃

> **設計規範**：
> - 所有 PK 統一為 `int IDENTITY(1,1)`
> - 所有時間欄位用 `datetime2`，預設 `GETUTCDATE()`
> - 軟刪除採 `IsDeleted` + `DeletedAt` 模式
> - FK 命名慣例：`{目標表名單數}Id`
> - 固定選項欄位優先使用 `tinyint`（附註解對照），程式碼以 C# enum 對應
> - Boolean 欄位使用 `bit`，命名加 `Is` / `Has` 前綴

---

### 表 1：Users（使用者帳號表）

```sql
-- 使用者帳號表：儲存所有內部人員與外部教師的帳號資訊
CREATE TABLE Users (
    Id              int IDENTITY(1,1) PRIMARY KEY,        -- 使用者唯一識別碼
    Account         nvarchar(50)  NOT NULL UNIQUE,         -- 登入帳號（內部人員為自訂帳號，外部教師為信箱）
    PasswordHash    nvarchar(256) NOT NULL,                -- 密碼雜湊值（bcrypt/Argon2）
    Name            nvarchar(50)  NOT NULL,                -- 顯示名稱
    Email           nvarchar(100) NULL,                    -- 電子信箱（忘記密碼用）
    RoleId          int           NOT NULL,                -- FK → Roles.Id，系統角色
    IsInternal      bit           NOT NULL DEFAULT 1,      -- 1=內部人員, 0=外部人員
    CompanyTitle    nvarchar(100) NULL,                    -- 公司職稱（僅內部人員使用）
    Note            nvarchar(500) NULL,                    -- 備註
    IsActive        bit           NOT NULL DEFAULT 1,      -- 帳號是否啟用
    IsFirstLogin    bit           NOT NULL DEFAULT 1,      -- 是否為首次登入（強制改密碼）
    RememberToken   nvarchar(256) NULL,                    -- 記住登入狀態的 Token
    RememberExpiry  datetime2     NULL,                    -- 記住登入 Token 到期時間
    LastLoginAt     datetime2     NULL,                    -- 最後登入時間
    LastProjectId   int           NULL,                    -- FK → Projects.Id，上次離開時的梯次
    CreatedAt       datetime2     NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt       datetime2     NOT NULL DEFAULT GETUTCDATE(),

    CONSTRAINT FK_Users_Roles FOREIGN KEY (RoleId) REFERENCES Roles(Id),
    CONSTRAINT FK_Users_LastProject FOREIGN KEY (LastProjectId) REFERENCES Projects(Id)
);
CREATE INDEX IX_Users_Account ON Users(Account);
CREATE INDEX IX_Users_RoleId ON Users(RoleId);
CREATE INDEX IX_Users_Email ON Users(Email);
```

---

### 表 2：Roles（角色表）

```sql
-- 角色表：定義系統中所有角色（內部人員 + 外部人員）
CREATE TABLE Roles (
    Id              int IDENTITY(1,1) PRIMARY KEY,        -- 角色唯一識別碼
    Name            nvarchar(50)  NOT NULL UNIQUE,         -- 角色名稱（如：系統管理員、命題教師）
    Code            nvarchar(20)  NOT NULL UNIQUE,         -- 程式代碼（TEACHER/REVIEWER/CHIEF/ADMIN/PI/ACADEMIC）
    IsInternal      bit           NOT NULL,                -- 1=內部人員角色, 0=外部人員角色
    Description     nvarchar(500) NULL,                    -- 角色描述
    IsDefault       bit           NOT NULL DEFAULT 0,      -- 預設角色不可刪除、不可修改權限
    CreatedAt       datetime2     NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt       datetime2     NOT NULL DEFAULT GETUTCDATE()
);
```

**Seed Data：**
| Id | Name | Code | IsInternal | IsDefault |
|----|------|------|:----------:|:---------:|
| 1 | 命題教師 | TEACHER | 0 | 1 |
| 2 | 審題委員 | REVIEWER | 0 | 1 |
| 3 | 總召 | CHIEF | 1 | 1 |
| 4 | 系統管理員 | ADMIN | 1 | 0 |
| 5 | 計畫主持人 | PI | 1 | 0 |
| 6 | 教務管理者 | ACADEMIC | 1 | 0 |

---

### 表 3：RolePermissions（角色功能權限表）

```sql
-- 角色功能權限表：定義各角色對 8 大功能模組的存取權限
CREATE TABLE RolePermissions (
    Id                int IDENTITY(1,1) PRIMARY KEY,       -- 唯一識別碼
    RoleId            int           NOT NULL,               -- FK → Roles.Id
    ModuleKey         nvarchar(50)  NOT NULL,               -- 功能模組 Key（見下方 8 模組定義）
    IsEnabled         bit           NOT NULL DEFAULT 0,     -- 是否開啟此模組存取
    AnnouncementPerm  tinyint       NULL DEFAULT 0,         -- 公告權限層級：0=view, 1=edit（僅 announcements 模組使用）
    CreatedAt         datetime2     NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt         datetime2     NOT NULL DEFAULT GETUTCDATE(),

    CONSTRAINT FK_RolePermissions_Roles FOREIGN KEY (RoleId) REFERENCES Roles(Id) ON DELETE CASCADE,
    CONSTRAINT UQ_RolePermissions UNIQUE (RoleId, ModuleKey)
);
```

**8 個模組 Key：**
| ModuleKey | 功能名稱 |
|-----------|---------|
| `dashboard` | 命題儀表板 |
| `projects` | 命題專案管理 |
| `overview` | 命題總覽 |
| `compose` | 命題任務 |
| `review` | 審題任務 |
| `teachers` | 教師管理系統 |
| `roles` | 角色與權限管理 |
| `announcements` | 系統公告/使用說明 |

---

### 表 4：PasswordResetTokens（密碼重設 Token 表）

```sql
-- 密碼重設 Token 表：忘記密碼功能的 GUID Token 記錄
CREATE TABLE PasswordResetTokens (
    Id          int IDENTITY(1,1) PRIMARY KEY,             -- 唯一識別碼
    UserId      int           NOT NULL,                    -- FK → Users.Id
    Token       nvarchar(128) NOT NULL UNIQUE,             -- GUID Token（信件連結識別碼，建議有效期 10 分鐘）
    ExpiresAt   datetime2     NOT NULL,                    -- Token 到期時間
    IsUsed      bit           NOT NULL DEFAULT 0,          -- 是否已使用
    CreatedAt   datetime2     NOT NULL DEFAULT GETUTCDATE(),

    CONSTRAINT FK_PasswordResetTokens_Users FOREIGN KEY (UserId) REFERENCES Users(Id)
);
CREATE INDEX IX_PRT_Token ON PasswordResetTokens(Token);
CREATE INDEX IX_PRT_UserId ON PasswordResetTokens(UserId);
```

---

### 表 5：LoginLogs（登入日誌表）

```sql
-- 登入日誌表：記錄所有登入嘗試（成功與失敗）
CREATE TABLE LoginLogs (
    Id          int IDENTITY(1,1) PRIMARY KEY,             -- 唯一識別碼
    UserId      int           NOT NULL,                    -- FK → Users.Id
    LoginAt     datetime2     NOT NULL DEFAULT GETUTCDATE(), -- 登入時間
    IpAddress   nvarchar(45)  NULL,                        -- 登入 IP（支援 IPv6 最大 45 字元）
    UserAgent   nvarchar(500) NULL,                        -- 瀏覽器 UserAgent
    IsSuccess   bit           NOT NULL,                    -- 是否登入成功
    FailReason  nvarchar(100) NULL,                        -- 失敗原因（如：密碼錯誤、驗證碼錯誤）

    CONSTRAINT FK_LoginLogs_Users FOREIGN KEY (UserId) REFERENCES Users(Id)
);
CREATE INDEX IX_LoginLogs_UserId ON LoginLogs(UserId, LoginAt DESC);
```

---

### 表 6：Projects（命題專案/梯次表）

```sql
-- 命題專案/梯次表：管理產學合作命題專案的完整生命週期
CREATE TABLE Projects (
    Id          int IDENTITY(1,1) PRIMARY KEY,             -- 專案唯一識別碼
    Code        nvarchar(20)  NOT NULL UNIQUE,             -- 專案代碼（如 P2026-01）
    Name        nvarchar(100) NOT NULL,                    -- 專案名稱（如「115年度 春季全民中檢」）
    Year        nvarchar(10)  NOT NULL,                    -- 年度（如「115」，民國年）
    Status      tinyint       NOT NULL DEFAULT 0,          -- 狀態：0=preparing, 1=active, 2=closed
    SchoolName  nvarchar(100) NULL,                        -- 合作學校名稱（NULL 表示自辦）
    StartDate   date          NULL,                        -- 產學計畫起始日
    EndDate     date          NULL,                        -- 產學計畫結束日
    ClosedAt    datetime2     NULL,                        -- 實際結案時間
    CreatedAt   datetime2     NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt   datetime2     NOT NULL DEFAULT GETUTCDATE()
);
CREATE INDEX IX_Projects_Status ON Projects(Status);
```

---

### 表 7：ProjectPhases（專案階段時程表）

```sql
-- 專案階段時程表：每個專案 7 個階段的起迄日期設定
CREATE TABLE ProjectPhases (
    Id          int IDENTITY(1,1) PRIMARY KEY,             -- 唯一識別碼
    ProjectId   int           NOT NULL,                    -- FK → Projects.Id
    PhaseCode   tinyint       NOT NULL,                    -- 階段代碼：1=proposition, 2=cross_review, 3=cross_revision, 4=expert_review, 5=expert_revision, 6=chief_review, 7=chief_revision
    PhaseName   nvarchar(50)  NOT NULL,                    -- 階段中文名稱
    StartDate   date          NOT NULL,                    -- 階段起始日
    EndDate     date          NOT NULL,                    -- 階段結束日
    SortOrder   tinyint       NOT NULL,                    -- 排序順序（與 PhaseCode 值相同）

    CONSTRAINT FK_ProjectPhases_Projects FOREIGN KEY (ProjectId) REFERENCES Projects(Id) ON DELETE CASCADE,
    CONSTRAINT UQ_ProjectPhases UNIQUE (ProjectId, PhaseCode)
);
CREATE INDEX IX_ProjectPhases_ProjectId ON ProjectPhases(ProjectId, SortOrder);
```

**階段定義（每個專案 7 筆）：**
| PhaseCode | SortOrder | PhaseName |
|:---------:|:---------:|-----------|
| 1 | 1 | 命題階段 |
| 2 | 2 | 交互審題 |
| 3 | 3 | 互審修題 |
| 4 | 4 | 專家審題 |
| 5 | 5 | 專審修題 |
| 6 | 6 | 總召審題 |
| 7 | 7 | 總召修題 |

---

### 表 8：ProjectMembers（專案成員指派表）

```sql
-- 專案成員指派表：記錄哪些使用者被指派到哪個專案
CREATE TABLE ProjectMembers (
    Id          int IDENTITY(1,1) PRIMARY KEY,             -- 唯一識別碼
    ProjectId   int           NOT NULL,                    -- FK → Projects.Id
    UserId      int           NOT NULL,                    -- FK → Users.Id
    CreatedAt   datetime2     NOT NULL DEFAULT GETUTCDATE(),

    CONSTRAINT FK_ProjectMembers_Projects FOREIGN KEY (ProjectId) REFERENCES Projects(Id),
    CONSTRAINT FK_ProjectMembers_Users FOREIGN KEY (UserId) REFERENCES Users(Id),
    CONSTRAINT UQ_ProjectMembers UNIQUE (ProjectId, UserId)
);
```

---

### 表 9：ProjectMemberRoles（專案成員角色關聯表）

```sql
-- 專案成員角色關聯表：支援同一成員在同一專案中擔任多個角色
CREATE TABLE ProjectMemberRoles (
    Id              int IDENTITY(1,1) PRIMARY KEY,         -- 唯一識別碼
    ProjectMemberId int           NOT NULL,                -- FK → ProjectMembers.Id
    RoleCode        tinyint       NOT NULL,                -- 角色代碼：1=TEACHER, 2=CROSS_REVIEWER, 3=EXPERT_REVIEWER, 4=CHIEF, 5=INTERNAL

    CONSTRAINT FK_PMR_ProjectMembers FOREIGN KEY (ProjectMemberId) REFERENCES ProjectMembers(Id) ON DELETE CASCADE,
    CONSTRAINT UQ_PMR UNIQUE (ProjectMemberId, RoleCode)
);
```

**專案角色代碼：**
| RoleCode | 含義 | 說明 |
|:--------:|------|------|
| 1 | TEACHER | 命題教師 |
| 2 | CROSS_REVIEWER | 互審教師 |
| 3 | EXPERT_REVIEWER | 專審委員 |
| 4 | CHIEF | 總召(專員) |
| 5 | INTERNAL | 內部人員 |

---

### 表 10：ProjectTargets（專案題型目標數量表）

```sql
-- 專案題型目標數量表：各題型在此專案中需產出的目標題數
CREATE TABLE ProjectTargets (
    Id                  int IDENTITY(1,1) PRIMARY KEY,     -- 唯一識別碼
    ProjectId           int           NOT NULL,            -- FK → Projects.Id
    QuestionTypeCode    nvarchar(20)  NOT NULL,            -- FK → QuestionTypes.Code（single/select/readGroup/...）
    TargetCount         int           NOT NULL DEFAULT 0,  -- 目標題數

    CONSTRAINT FK_ProjectTargets_Projects FOREIGN KEY (ProjectId) REFERENCES Projects(Id) ON DELETE CASCADE,
    CONSTRAINT FK_ProjectTargets_Types FOREIGN KEY (QuestionTypeCode) REFERENCES QuestionTypes(Code),
    CONSTRAINT UQ_ProjectTargets UNIQUE (ProjectId, QuestionTypeCode)
);
```

---

### 表 11：MemberQuotas（人員命題配額表）

```sql
-- 人員命題配額表：各命題教師在專案中被分配的各題型命題數量
CREATE TABLE MemberQuotas (
    Id                  int IDENTITY(1,1) PRIMARY KEY,     -- 唯一識別碼
    ProjectMemberId     int           NOT NULL,            -- FK → ProjectMembers.Id
    QuestionTypeCode    nvarchar(20)  NOT NULL,            -- FK → QuestionTypes.Code
    Quota               int           NOT NULL DEFAULT 0,  -- 分配的命題數量

    CONSTRAINT FK_MemberQuotas_PM FOREIGN KEY (ProjectMemberId) REFERENCES ProjectMembers(Id) ON DELETE CASCADE,
    CONSTRAINT FK_MemberQuotas_Types FOREIGN KEY (QuestionTypeCode) REFERENCES QuestionTypes(Code),
    CONSTRAINT UQ_MemberQuotas UNIQUE (ProjectMemberId, QuestionTypeCode)
);
```

---

### 表 12：QuestionTypes（題型設定表）

```sql
-- 題型設定表：定義系統支援的 7 種題型及其特性（Seed Data，極少變動）
CREATE TABLE QuestionTypes (
    Code            nvarchar(20) PRIMARY KEY,              -- 題型代碼（以 Code 為 PK，唯一例外，因固定 Seed Data 且被多表 FK 引用）
    Name            nvarchar(20)  NOT NULL,                -- 題型中文名稱
    HasSubQuestions bit           NOT NULL DEFAULT 0,      -- 是否為題組類型（有子題）
    SubQuestionMode tinyint       NULL,                    -- 子題模式：NULL=無子題, 0=choice(選擇題), 1=freeResponse(論述題)
    HasAudio        bit           NOT NULL DEFAULT 0,      -- 是否有音檔
    HasPassage      bit           NOT NULL DEFAULT 0,      -- 是否有母題閱讀段落
    PassageLabel    nvarchar(20)  NULL                     -- 段落標籤（如「閱讀文本」「短文題幹」「聽力文本」）
);
```

**Seed Data（7 題型）：**
| Code | Name | HasSubQuestions | SubQuestionMode | HasAudio | HasPassage | PassageLabel |
|------|------|:---:|:---:|:---:|:---:|:---:|
| `single` | 一般單選題 | 0 | NULL | 0 | 0 | NULL |
| `select` | 精選單選題 | 0 | NULL | 0 | 0 | NULL |
| `longText` | 長文題目 | 0 | NULL | 0 | 0 | NULL |
| `readGroup` | 閱讀題組 | 1 | 0 (choice) | 0 | 1 | 閱讀文本 |
| `shortGroup` | 短文題組 | 1 | 1 (freeResponse) | 0 | 1 | 短文題幹 |
| `listen` | 聽力測驗 | 0 | NULL | 1 | 0 | NULL |
| `listenGroup` | 聽力題組 | 1 | 0 (choice) | 1 | 1 | 聽力文本 |

---

### 表 13：Questions（試題母題表）

```sql
-- 試題母題表：命題核心資料表，記錄每道試題的完整內容與狀態
CREATE TABLE Questions (
    Id                  int IDENTITY(1,1) PRIMARY KEY,     -- 試題唯一識別碼
    ProjectId           int           NOT NULL,            -- FK → Projects.Id，所屬梯次
    QuestionCode        nvarchar(20)  NOT NULL UNIQUE,     -- 題碼（如 Q-2603-001）
    QuestionTypeCode    nvarchar(20)  NOT NULL,            -- FK → QuestionTypes.Code
    Level               nvarchar(10)  NOT NULL,            -- 等級（初級/中級/中高級/高級/優級/難度一~五）
    Difficulty          tinyint       NOT NULL,            -- 難易度：0=easy, 1=medium, 2=hard
    AuthorId            int           NOT NULL,            -- FK → Users.Id，命題教師
    Stem                nvarchar(MAX) NULL,                -- 題幹內容（HTML）
    Passage             nvarchar(MAX) NULL,                -- 閱讀文本/聽力文本（題組母題用）
    AudioUrl            nvarchar(500) NULL,                -- 音訊檔案路徑（聽力題型用）
    Options             nvarchar(MAX) NULL,                -- 選項 JSON [{"label":"A","text":"..."},...]
    Answer              nvarchar(10)  NULL,                -- 正確答案代碼（如 A/B/C/D）
    Analysis            nvarchar(MAX) NULL,                -- 解析（HTML）
    CurrentStage        tinyint       NOT NULL DEFAULT 1,  -- 當前所在階段（1~7，對應 ProjectPhases.PhaseCode）
    Status              tinyint       NOT NULL DEFAULT 0,  -- 當前狀態碼（0~13，見附錄狀態碼對照表）
    IsDeleted           bit           NOT NULL DEFAULT 0,  -- 軟刪除標記
    DeletedAt           datetime2     NULL,                -- 軟刪除時間
    CreatedAt           datetime2     NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt           datetime2     NOT NULL DEFAULT GETUTCDATE(),

    CONSTRAINT FK_Questions_Projects FOREIGN KEY (ProjectId) REFERENCES Projects(Id),
    CONSTRAINT FK_Questions_Types FOREIGN KEY (QuestionTypeCode) REFERENCES QuestionTypes(Code),
    CONSTRAINT FK_Questions_Author FOREIGN KEY (AuthorId) REFERENCES Users(Id)
);
CREATE INDEX IX_Questions_ProjectId ON Questions(ProjectId);
CREATE INDEX IX_Questions_Status ON Questions(Status);
CREATE INDEX IX_Questions_AuthorId ON Questions(AuthorId);
CREATE INDEX IX_Questions_QuestionTypeCode ON Questions(QuestionTypeCode);
CREATE INDEX IX_Questions_ProjectId_Status ON Questions(ProjectId, Status) WHERE IsDeleted = 0;
```

> ⚠️ **v2.0 變更**：移除 `ReturnCount` 欄位（與 `ReviewReturnCounts.ReturnCount` 重複），統一由 `ReviewReturnCounts` 管理退回次數。

---

### 表 14：SubQuestions（子題表）

```sql
-- 子題表：題組類型（閱讀/短文/聽力題組）的子題內容
CREATE TABLE SubQuestions (
    Id          int IDENTITY(1,1) PRIMARY KEY,             -- 唯一識別碼
    QuestionId  int           NOT NULL,                    -- FK → Questions.Id，所屬母題
    SubIndex    int           NOT NULL,                    -- 子題序號（1, 2, 3...）
    SubCode     char(1)       NOT NULL,                    -- 子題代碼後綴（A, B, C...）
    Stem        nvarchar(MAX) NOT NULL,                    -- 子題題幹（HTML）
    Options     nvarchar(MAX) NULL,                        -- 選項 JSON（choice 模式使用）
    Answer      nvarchar(10)  NULL,                        -- 正確答案（A/B/C/D，論述題為 NULL）
    Analysis    nvarchar(MAX) NULL,                        -- 子題解析
    Dimension   nvarchar(20)  NULL,                        -- 向度（短文題組用：條列敘述/歸納統整/分析推理）
    Indicator   nvarchar(100) NULL,                        -- 細目指標（短文題組用）
    SortOrder   int           NOT NULL DEFAULT 0,          -- 排序用

    CONSTRAINT FK_SubQuestions_Questions FOREIGN KEY (QuestionId) REFERENCES Questions(Id) ON DELETE CASCADE,
    CONSTRAINT UQ_SubQuestions UNIQUE (QuestionId, SubIndex)
);
```

---

### 表 15：QuestionAttributes（試題屬性表）

```sql
-- 試題屬性表：與 Questions 一對一，儲存各題型專屬的分類屬性
CREATE TABLE QuestionAttributes (
    Id              int IDENTITY(1,1) PRIMARY KEY,         -- 唯一識別碼
    QuestionId      int           NOT NULL UNIQUE,         -- FK → Questions.Id (1:1)
    Topic           nvarchar(50)  NULL,                    -- 主類（一般/精選單選題：文字/語詞/成語短語/...）
    SubTopic        nvarchar(50)  NULL,                    -- 細目（依主類連動）
    Mode            nvarchar(50)  NULL,                    -- 作文模式（長文題目：引導寫作/資訊整合）
    Genre           nvarchar(20)  NULL,                    -- 文體（短文題組：文言文/應用文/語體文）
    MainCategory    nvarchar(50)  NULL,                    -- 主分類（短文題組：文義判讀）
    SubCategory     nvarchar(50)  NULL,                    -- 次分類（短文題組：篇章辨析）
    AudioType       nvarchar(20)  NULL,                    -- 音訊類型（聽力：對話/情境/陳述）
    Material        nvarchar(20)  NULL,                    -- 素材來源（聽力：生活/教育/職場/專業）
    Competency      nvarchar(50)  NULL,                    -- 核心能力（聽力，依等級自動連動）
    Indicator       nvarchar(100) NULL,                    -- 細目指標

    CONSTRAINT FK_QuestionAttributes_Questions FOREIGN KEY (QuestionId) REFERENCES Questions(Id) ON DELETE CASCADE
);
```

---

### 表 16：QuestionHistoryLogs（試題歷程軌跡表）

```sql
-- 試題歷程軌跡表：記錄試題從建立到結案的完整操作歷程
CREATE TABLE QuestionHistoryLogs (
    Id          int IDENTITY(1,1) PRIMARY KEY,             -- 唯一識別碼
    QuestionId  int           NOT NULL,                    -- FK → Questions.Id（歷程為母題級別）
    UserId      int           NOT NULL,                    -- FK → Users.Id，操作人員
    Action      nvarchar(50)  NOT NULL,                    -- 動作描述（如「命題完成」「互審意見」「專審意見（採用）」「總召決策（改後再審）」）
    Comment     nvarchar(MAX) NULL,                        -- 審查意見內容
    CreatedAt   datetime2     NOT NULL DEFAULT GETUTCDATE(), -- 操作時間

    CONSTRAINT FK_QHL_Questions FOREIGN KEY (QuestionId) REFERENCES Questions(Id),
    CONSTRAINT FK_QHL_Users FOREIGN KEY (UserId) REFERENCES Users(Id)
);
CREATE INDEX IX_QHL_QuestionId ON QuestionHistoryLogs(QuestionId, CreatedAt DESC);
```

---

### 表 17：RevisionReplies（修題回覆表）

```sql
-- 修題回覆表：命題教師在各審修階段收到退回後的修題回覆說明
CREATE TABLE RevisionReplies (
    Id              int IDENTITY(1,1) PRIMARY KEY,         -- 唯一識別碼
    QuestionId      int           NOT NULL,                -- FK → Questions.Id
    ReviewStage     tinyint       NOT NULL,                -- 審查階段：1=peer, 2=expert, 3=final
    ReplyContent    nvarchar(MAX) NOT NULL,                -- 修題回覆內容（HTML）
    RepliedBy       int           NOT NULL,                -- FK → Users.Id，回覆者（命題教師）
    CreatedAt       datetime2     NOT NULL DEFAULT GETUTCDATE(),

    CONSTRAINT FK_RevisionReplies_Questions FOREIGN KEY (QuestionId) REFERENCES Questions(Id),
    CONSTRAINT FK_RevisionReplies_Users FOREIGN KEY (RepliedBy) REFERENCES Users(Id)
);
CREATE INDEX IX_RevisionReplies_QuestionId ON RevisionReplies(QuestionId);
```

---

### 表 18：ReviewAssignments（審題分配表）

```sql
-- 審題分配表：記錄每道試題在各審查階段的審題者分配、決策結果與審查意見
CREATE TABLE ReviewAssignments (
    Id              int IDENTITY(1,1) PRIMARY KEY,         -- 唯一識別碼
    QuestionId      int           NOT NULL,                -- FK → Questions.Id，被審題目
    ProjectId       int           NOT NULL,                -- FK → Projects.Id（反正規化，避免每次 JOIN Questions 取 ProjectId）
    ReviewerId      int           NOT NULL,                -- FK → Users.Id，審題者
    ReviewStage     tinyint       NOT NULL,                -- 審查階段：1=peer, 2=expert, 3=final
    ReviewStatus    tinyint       NOT NULL DEFAULT 0,      -- 審查狀態：0=pending, 1=decided
    Decision        tinyint       NULL,                    -- 決策結果：0=comment(互審意見), 1=adopt(採用), 2=revise(改後再審), 3=reject(不採用)
    Comment         nvarchar(MAX) NULL,                    -- 審查意見（HTML）
    DecidedAt       datetime2     NULL,                    -- 決策時間
    CreatedAt       datetime2     NOT NULL DEFAULT GETUTCDATE(),

    CONSTRAINT FK_RA_Questions FOREIGN KEY (QuestionId) REFERENCES Questions(Id),
    CONSTRAINT FK_RA_Projects FOREIGN KEY (ProjectId) REFERENCES Projects(Id),
    CONSTRAINT FK_RA_Reviewers FOREIGN KEY (ReviewerId) REFERENCES Users(Id),
    CONSTRAINT UQ_RA UNIQUE (QuestionId, ReviewerId, ReviewStage)
);
CREATE INDEX IX_RA_QuestionId ON ReviewAssignments(QuestionId);
CREATE INDEX IX_RA_ReviewerId ON ReviewAssignments(ReviewerId, ReviewStatus);
CREATE INDEX IX_RA_ProjectId_Stage ON ReviewAssignments(ProjectId, ReviewStage, ReviewStatus);
```

---

### 表 19：ReviewReturnCounts（總審退回次數追蹤表）

```sql
-- 總審退回次數追蹤表：追蹤總審階段的退回次數，第 3 次不退回改由總召自行修改
CREATE TABLE ReviewReturnCounts (
    Id                  int IDENTITY(1,1) PRIMARY KEY,     -- 唯一識別碼
    QuestionId          int           NOT NULL,            -- FK → Questions.Id
    FinalReviewerId     int           NOT NULL,            -- FK → Users.Id，總審審題者
    ReturnCount         tinyint       NOT NULL DEFAULT 0,  -- 退回次數（最大 2，第 3 次不退回）
    CanEditByReviewer   bit           NOT NULL DEFAULT 0,  -- 總審是否取得編輯權限（ReturnCount >= 2 時為 1）

    CONSTRAINT FK_RRC_Questions FOREIGN KEY (QuestionId) REFERENCES Questions(Id),
    CONSTRAINT FK_RRC_Users FOREIGN KEY (FinalReviewerId) REFERENCES Users(Id),
    CONSTRAINT UQ_RRC UNIQUE (QuestionId, FinalReviewerId)
);
```

---

### 表 20：SimilarityChecks（試題比對記錄表）

```sql
-- 試題比對記錄表：記錄 AI 引擎的試題相似度比對結果
CREATE TABLE SimilarityChecks (
    Id                  int IDENTITY(1,1) PRIMARY KEY,     -- 唯一識別碼
    SourceQuestionId    int           NOT NULL,            -- FK → Questions.Id，來源題目
    ComparedQuestionId  int           NOT NULL,            -- FK → Questions.Id，比對題目
    SimilarityScore     decimal(5,2)  NOT NULL,            -- 相似度百分比（0.00~100.00）
    Determination       tinyint       NOT NULL,            -- 判定結果：0=無疑慮, 1=低度相似, 2=高度相似
    CheckedBy           int           NULL,                -- FK → Users.Id，發起比對者
    CheckedAt           datetime2     NOT NULL DEFAULT GETUTCDATE(), -- 比對時間

    CONSTRAINT FK_SC_Source FOREIGN KEY (SourceQuestionId) REFERENCES Questions(Id),
    CONSTRAINT FK_SC_Compared FOREIGN KEY (ComparedQuestionId) REFERENCES Questions(Id),
    CONSTRAINT FK_SC_User FOREIGN KEY (CheckedBy) REFERENCES Users(Id)
);
CREATE INDEX IX_SC_SourceQuestionId ON SimilarityChecks(SourceQuestionId);
```

---

### 表 21：CannedMessages（罐頭訊息表）

```sql
-- 罐頭訊息表：審題時可快速插入的預設審查意見範本
CREATE TABLE CannedMessages (
    Id          int IDENTITY(1,1) PRIMARY KEY,             -- 唯一識別碼
    Content     nvarchar(500) NOT NULL,                    -- 訊息內容
    SortOrder   int           NOT NULL DEFAULT 0,          -- 排序順序
    IsActive    bit           NOT NULL DEFAULT 1           -- 是否啟用
);
```

---

### 表 22：Teachers（教師人才庫表）

```sql
-- 教師人才庫表：外部教師的延伸資料，與 Users 表一對一關聯
CREATE TABLE Teachers (
    Id              int IDENTITY(1,1) PRIMARY KEY,         -- 唯一識別碼
    UserId          int           NOT NULL UNIQUE,         -- FK → Users.Id (1:1)，帳號合併機制核心
    TeacherCode     nvarchar(10)  NOT NULL UNIQUE,         -- 教師編號（如 T1001，自動遞增產生）
    Gender          tinyint       NULL,                    -- 性別：0=男, 1=女
    Phone           nvarchar(20)  NULL,                    -- 聯絡電話
    IdNumber        nvarchar(256) NULL,                    -- 身分證字號（加密儲存，前端遮罩顯示）
    School          nvarchar(100) NOT NULL,                -- 任教學校
    Department      nvarchar(50)  NULL,                    -- 系所/科別
    Title           nvarchar(20)  NULL,                    -- 職稱（教授/副教授/助理教授/講師/教師/兼任教師）
    Expertise       nvarchar(200) NULL,                    -- 專長領域（自由文字描述）
    TeachingYears   int           NULL,                    -- 教學年資
    Education       tinyint       NULL,                    -- 最高學歷：0=學士, 1=碩士, 2=博士
    Note            nvarchar(500) NULL,                    -- 備註
    CreatedAt       datetime2     NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt       datetime2     NOT NULL DEFAULT GETUTCDATE(),

    CONSTRAINT FK_Teachers_Users FOREIGN KEY (UserId) REFERENCES Users(Id)
);
CREATE INDEX IX_Teachers_UserId ON Teachers(UserId);
```

---

### 表 23：Announcements（系統公告表）

```sql
-- 系統公告表：管理公告的 CRUD，支援置頂、自動下架、草稿、梯次綁定等機制
CREATE TABLE Announcements (
    Id              int IDENTITY(1,1) PRIMARY KEY,         -- 唯一識別碼
    Category        tinyint       NOT NULL,                -- 分類：0=system(系統公告), 1=compose(命題公告), 2=review(審題公告), 3=other(其它)
    Status          tinyint       NOT NULL DEFAULT 0,      -- 狀態：0=draft(草稿), 1=published(已發佈), 2=archived(已下架)
    ProjectId       int           NULL,                    -- FK → Projects.Id，NULL=全站廣播，有值=綁定特定梯次
    PublishDate     date          NOT NULL,                -- 預定發佈日期（上架日期）
    UnpublishDate   date          NULL,                    -- 預定下架日期（NULL=不自動下架）
    PublishedAt     datetime2     NULL,                    -- 實際發佈時間
    IsPinned        bit           NOT NULL DEFAULT 0,      -- 是否置頂
    Title           nvarchar(200) NOT NULL,                -- 公告標題
    Content         nvarchar(MAX) NOT NULL,                -- 公告內容（HTML Rich Text）
    AuthorId        int           NOT NULL,                -- FK → Users.Id，建立者
    CreatedAt       datetime2     NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt       datetime2     NOT NULL DEFAULT GETUTCDATE(),

    CONSTRAINT FK_Announcements_Projects FOREIGN KEY (ProjectId) REFERENCES Projects(Id),
    CONSTRAINT FK_Announcements_Author FOREIGN KEY (AuthorId) REFERENCES Users(Id)
);
CREATE INDEX IX_Announcements_Status ON Announcements(Status, IsPinned DESC, PublishDate DESC);
CREATE INDEX IX_Announcements_ProjectId ON Announcements(ProjectId);
```

---

### 表 24：UserGuideFiles（使用說明手冊表）

```sql
-- 使用說明手冊表：管理 PDF 使用說明手冊的上傳與下載
CREATE TABLE UserGuideFiles (
    Id              int IDENTITY(1,1) PRIMARY KEY,         -- 唯一識別碼
    FileName        nvarchar(200) NOT NULL,                -- 檔案顯示名稱
    FilePath        nvarchar(500) NOT NULL,                -- 儲存路徑或 Blob URL
    FileSize        bigint        NOT NULL,                -- 檔案大小（bytes）
    UploadedBy      int           NOT NULL,                -- FK → Users.Id，上傳者
    IsActive        bit           NOT NULL DEFAULT 1,      -- 是否為當前可下載版本
    CreatedAt       datetime2     NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt       datetime2     NOT NULL DEFAULT GETUTCDATE(),

    CONSTRAINT FK_UserGuideFiles_Users FOREIGN KEY (UploadedBy) REFERENCES Users(Id)
);
```

---

### 表 25：UrgentReminders（急件提醒表）

```sql
-- 急件提醒表：首頁今日提醒看板的急件/到期警示資料來源
CREATE TABLE UrgentReminders (
    Id              int IDENTITY(1,1) PRIMARY KEY,         -- 唯一識別碼
    ProjectId       int           NOT NULL,                -- FK → Projects.Id，所屬專案
    PhaseCode       tinyint       NOT NULL,                -- 觸發階段（1~7，對應 ProjectPhases.PhaseCode）
    TargetRoleCode  tinyint       NULL,                    -- 對象角色代碼（NULL=全角色）
    TargetUserId    int           NULL,                    -- FK → Users.Id（NULL=全員）
    Message         nvarchar(500) NOT NULL,                -- 提醒訊息
    LinkUrl         nvarchar(200) NULL,                    -- 點擊跳轉連結
    IsActive        bit           NOT NULL DEFAULT 1,      -- 是否有效
    CreatedAt       datetime2     NOT NULL DEFAULT GETUTCDATE(),

    CONSTRAINT FK_UR_Projects FOREIGN KEY (ProjectId) REFERENCES Projects(Id),
    CONSTRAINT FK_UR_Users FOREIGN KEY (TargetUserId) REFERENCES Users(Id)
);
CREATE INDEX IX_UR_ProjectId ON UrgentReminders(ProjectId, IsActive);
```

---

### 表 26：AuditLogs（操作日誌表）

```sql
-- 操作日誌表：記錄系統中所有重要操作的稽核軌跡
CREATE TABLE AuditLogs (
    Id              int IDENTITY(1,1) PRIMARY KEY,         -- 唯一識別碼
    UserId          int           NOT NULL,                -- FK → Users.Id，操作者
    ProjectId       int           NULL,                    -- FK → Projects.Id，相關專案
    Action          tinyint       NOT NULL,                -- 動作類型：0=CREATE, 1=UPDATE, 2=DELETE, 3=LOGIN, 4=LOGOUT, 5=SYSTEM
    TargetTable     nvarchar(50)  NULL,                    -- 目標資料表名稱
    TargetId        int           NULL,                    -- 目標記錄 ID
    Description     nvarchar(500) NULL,                    -- 操作描述
    OldValue        nvarchar(MAX) NULL,                    -- 變更前的值（JSON）
    NewValue        nvarchar(MAX) NULL,                    -- 變更後的值（JSON）
    IpAddress       nvarchar(45)  NULL,                    -- 操作者 IP
    CreatedAt       datetime2     NOT NULL DEFAULT GETUTCDATE(),

    CONSTRAINT FK_AuditLogs_Users FOREIGN KEY (UserId) REFERENCES Users(Id),
    CONSTRAINT FK_AuditLogs_Projects FOREIGN KEY (ProjectId) REFERENCES Projects(Id)
);
CREATE INDEX IX_AuditLogs_UserId ON AuditLogs(UserId, CreatedAt DESC);
CREATE INDEX IX_AuditLogs_ProjectId ON AuditLogs(ProjectId, CreatedAt DESC);
CREATE INDEX IX_AuditLogs_Action ON AuditLogs(Action, CreatedAt DESC);
```

---

## 四、資料表關聯圖（ERD Text）

```
┌─────────────────────────────────────────────────────────────────────┐
│                          核心身份層                                  │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  Roles ──1:N──► RolePermissions                                     │
│    │                                                                │
│    │ 1:N                                                            │
│    ▼                                                                │
│  Users ──1:1──► Teachers（外部教師延伸資料）                          │
│    │                                                                │
│    ├──1:N──► LoginLogs                                              │
│    ├──1:N──► PasswordResetTokens                                    │
│    └──1:N──► AuditLogs                                              │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                          專案管理層                                  │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  Projects                                                           │
│    ├──1:N──► ProjectPhases（7 階段時程）                              │
│    ├──1:N──► ProjectTargets（題型目標數量）                            │
│    ├──1:N──► ProjectMembers                                         │
│    │              ├──1:N──► ProjectMemberRoles（多角色）              │
│    │              └──1:N──► MemberQuotas（命題配額）                  │
│    ├──1:N──► Announcements                                          │
│    └──1:N──► UrgentReminders                                        │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                          試題核心層                                  │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  QuestionTypes（7 題型定義）                                         │
│    │                                                                │
│    │ 1:N                                                            │
│    ▼                                                                │
│  Questions                                                          │
│    ├──1:N──► SubQuestions（子題）                                    │
│    ├──1:1──► QuestionAttributes（擴充屬性）                          │
│    ├──1:N──► QuestionHistoryLogs（歷程軌跡）                         │
│    ├──1:N──► RevisionReplies（修題回覆）                             │
│    ├──1:N──► ReviewAssignments（審題分配）                           │
│    ├──1:N──► ReviewReturnCounts（退回次數追蹤）                       │
│    └──1:N──► SimilarityChecks（相似度比對）                          │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                          獨立功能層                                  │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  CannedMessages（罐頭訊息，無 FK）                                   │
│  UserGuideFiles（使用說明手冊）                                      │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 五、View / Stored Procedure 建議

### View 1：DashboardSummaryView（儀表板彙總）

```sql
CREATE VIEW DashboardSummaryView AS
SELECT
    q.ProjectId,
    q.QuestionTypeCode,
    COUNT(*)                                                    AS TotalCount,
    SUM(CASE WHEN q.Status = 12 THEN 1 ELSE 0 END)            AS AdoptedCount,      -- 12=adopted
    SUM(CASE WHEN q.Status IN (3,5,6,8,9,11) THEN 1 ELSE 0 END)
                                                                AS ReviewingCount,    -- 審修中各階段
    SUM(CASE WHEN q.Status IN (0,1) THEN 1 ELSE 0 END)        AS DraftCount          -- 0=draft, 1=completed
FROM Questions q
WHERE q.IsDeleted = 0
GROUP BY q.ProjectId, q.QuestionTypeCode;
```

### View 2：OverdueTasksView（逾期待辦）

```sql
CREATE VIEW OverdueTasksView AS
SELECT
    q.Id AS QuestionId,
    q.ProjectId,
    q.QuestionCode,
    q.QuestionTypeCode,
    q.Status,
    q.AuthorId,
    u.Name AS AuthorName,
    pp.EndDate AS PhaseEndDate,
    DATEDIFF(DAY, pp.EndDate, CAST(GETUTCDATE() AS DATE)) AS OverdueDays
FROM Questions q
JOIN Users u ON u.Id = q.AuthorId
JOIN ProjectPhases pp ON pp.ProjectId = q.ProjectId
    AND pp.SortOrder = q.CurrentStage
WHERE q.IsDeleted = 0
    AND q.Status NOT IN (12, 13, 0, 1)    -- 排除 adopted, rejected, draft, completed
    AND pp.EndDate < CAST(GETUTCDATE() AS DATE);
```

### View 3：QuestionOverviewView（命題總覽用）

```sql
-- 命題總覽頁面用：展開子題為獨立列，支援題組呈現規則
CREATE VIEW QuestionOverviewView AS
-- 非題組：一題一列
SELECT
    q.Id AS QuestionId,
    NULL AS SubQuestionId,
    q.QuestionCode AS DisplayCode,
    0 AS SubIndex,
    q.ProjectId,
    q.QuestionTypeCode,
    q.Level,
    q.Difficulty,
    q.AuthorId,
    u.Name AS AuthorName,
    q.CurrentStage,
    q.Status,
    q.CreatedAt
FROM Questions q
JOIN Users u ON u.Id = q.AuthorId
JOIN QuestionTypes qt ON qt.Code = q.QuestionTypeCode
WHERE q.IsDeleted = 0 AND qt.HasSubQuestions = 0

UNION ALL

-- 題組：每個子題各佔一列
SELECT
    q.Id AS QuestionId,
    sq.Id AS SubQuestionId,
    q.QuestionCode + '-' + sq.SubCode AS DisplayCode,
    sq.SubIndex,
    q.ProjectId,
    q.QuestionTypeCode,
    q.Level,
    q.Difficulty,
    q.AuthorId,
    u.Name AS AuthorName,
    q.CurrentStage,
    q.Status,
    q.CreatedAt
FROM Questions q
JOIN Users u ON u.Id = q.AuthorId
JOIN QuestionTypes qt ON qt.Code = q.QuestionTypeCode
JOIN SubQuestions sq ON sq.QuestionId = q.Id
WHERE q.IsDeleted = 0 AND qt.HasSubQuestions = 1;
```

---

## 六、待各 PRD 同步修正事項

| # | 影響 PRD | 修正內容 |
|---|---------|---------|
| 1 | PRD-Login | PK 改 int；Roles 表 `Category` → `IsInternal` bit；移除 `IsSystem` 改 `IsDefault`；RolePermissions 改模組級；移除「教師帳號由角色管理建立」描述 |
| 2 | PRD-FirstPage | Announcements：`IsTop` → `IsPinned`，`IsPublished` → `Status` tinyint；ProjectMembers 移除 `AssignedRoleCode`；Category `proposition` → `compose` |
| 3 | PRD-Dashboard | 狀態碼改 tinyint：12=adopted, 2=pending, 3=peer_reviewing 等；View SQL 同步更新 |
| 4 | PRD-Overview | PK 改 int；狀態碼改 tinyint；移除 `ReturnCount` 欄位 |
| 5 | PRD-Projects | PK 改 int；Projects.Status 改 tinyint |
| 6 | PRD-CwtList | PK 改 int；Questions.Status/Difficulty 改 tinyint |
| 7 | PRD-Reviews | PK 改 int；ReviewStage/Decision 改 tinyint；前端階段 key 加上 mapping 說明 |
| 8 | PRD-Teachers | PK 改 int；Gender/Education 改 tinyint |
| 9 | PRD-Roles | ✅ 已是 int PK；加入 `Code` 欄位；`Category` → `IsInternal` bit |
| 10 | PRD-Announcements | ✅ 已是 int PK；Status/Category 改 tinyint |

---

## 七、附錄：狀態碼對照表

### A. Questions.Status（試題狀態碼）

| 值 | 代碼 | 顯示標籤 | Badge 色 | 說明 |
|:--:|------|---------|---------|------|
| 0 | draft | 草稿 | 灰 | 教師命題中 |
| 1 | completed | 命題完成 | 藍 | 教師完成但未送出 |
| 2 | pending | 待審 | 黃 | 已提交等待分配 |
| 3 | peer_reviewing | 互審中 | Morandi 藍 | 交互審題階段 |
| 4 | peer_reviewed | 互審完成 | 綠 | 互審意見已提交 |
| 5 | peer_editing | 互審修題 | Terracotta 橘 | 教師依互審意見修改 |
| 6 | expert_reviewing | 專審中 | Morandi 藍 | 專家審題階段 |
| 7 | expert_reviewed | 專審完成 | 綠 | 專審意見已提交 |
| 8 | expert_editing | 專審修題 | Terracotta 橘 | 教師依專審意見修改 |
| 9 | final_reviewing | 總審中 | Morandi 藍 | 總召審題階段 |
| 10 | final_reviewed | 總審完成 | 紅 | 總審意見已提交 |
| 11 | final_editing | 總審修題 | Terracotta 橘 | 教師/總召修改 |
| 12 | adopted | 採用 | Sage 綠 | 試題入庫 |
| 13 | rejected | 不採用 | 灰 | 試題淘汰 |

### B. Questions.Difficulty（難易度）

| 值 | 代碼 | 說明 |
|:--:|------|------|
| 0 | easy | 易 |
| 1 | medium | 中 |
| 2 | hard | 難 |

### C. Projects.Status（專案狀態）

| 值 | 代碼 | 說明 |
|:--:|------|------|
| 0 | preparing | 準備中 |
| 1 | active | 進行中 |
| 2 | closed | 已結案 |

### D. Announcements.Status（公告狀態）

| 值 | 代碼 | 說明 |
|:--:|------|------|
| 0 | draft | 草稿 |
| 1 | published | 已發佈 |
| 2 | archived | 已下架 |

### E. Announcements.Category（公告分類）

| 值 | 代碼 | 說明 |
|:--:|------|------|
| 0 | system | 系統公告 |
| 1 | compose | 命題公告 |
| 2 | review | 審題公告 |
| 3 | other | 其它 |

### F. ReviewAssignments.ReviewStage / RevisionReplies.ReviewStage（審查階段）

| 值 | 代碼 | 說明 |
|:--:|------|------|
| 1 | peer | 互審 |
| 2 | expert | 專審 |
| 3 | final | 總審 |

### G. ReviewAssignments.Decision（審查決策）

| 值 | 代碼 | 說明 |
|:--:|------|------|
| 0 | comment | 互審意見（無裁決權） |
| 1 | adopt | 採用 |
| 2 | revise | 改後再審 |
| 3 | reject | 不採用 |

### H. ProjectPhases.PhaseCode（專案階段代碼）

| 值 | 代碼 | PhaseName |
|:--:|------|-----------|
| 1 | proposition | 命題階段 |
| 2 | cross_review | 交互審題 |
| 3 | cross_revision | 互審修題 |
| 4 | expert_review | 專家審題 |
| 5 | expert_revision | 專審修題 |
| 6 | chief_review | 總召審題 |
| 7 | chief_revision | 總召修題 |

### I. ProjectMemberRoles.RoleCode（專案成員角色）

| 值 | 代碼 | 說明 |
|:--:|------|------|
| 1 | TEACHER | 命題教師 |
| 2 | CROSS_REVIEWER | 互審教師 |
| 3 | EXPERT_REVIEWER | 專審委員 |
| 4 | CHIEF | 總召(專員) |
| 5 | INTERNAL | 內部人員 |

### J. AuditLogs.Action（操作日誌動作類型）

| 值 | 代碼 | 說明 |
|:--:|------|------|
| 0 | CREATE | 新增操作 |
| 1 | UPDATE | 更新操作 |
| 2 | DELETE | 刪除操作 |
| 3 | LOGIN | 使用者登入 |
| 4 | LOGOUT | 使用者登出 |
| 5 | SYSTEM | 系統自動操作 |

### K. SimilarityChecks.Determination（相似度判定）

| 值 | 代碼 | 說明 |
|:--:|------|------|
| 0 | none | 無疑慮 |
| 1 | low | 低度相似 |
| 2 | high | 高度相似 |

### L. Teachers.Gender（性別）

| 值 | 代碼 | 說明 |
|:--:|------|------|
| 0 | male | 男 |
| 1 | female | 女 |

### M. Teachers.Education（最高學歷）

| 值 | 代碼 | 說明 |
|:--:|------|------|
| 0 | bachelor | 學士 |
| 1 | master | 碩士 |
| 2 | doctor | 博士 |

### N. RolePermissions.AnnouncementPerm（公告權限層級）

| 值 | 代碼 | 說明 |
|:--:|------|------|
| 0 | view | 僅瀏覽 |
| 1 | edit | 瀏覽與編輯 |

### O. QuestionTypes.SubQuestionMode（子題模式）

| 值 | 代碼 | 說明 |
|:--:|------|------|
| 0 | choice | 選擇題 |
| 1 | freeResponse | 論述題 |

---

## 八、資料表總覽（26 張表 + 3 個 View）

| # | 資料表 | 筆數量級 | 主要用途 |
|---|--------|---------|---------|
| 1 | Users | 百 | 所有使用者帳號 |
| 2 | Roles | 十 | 角色定義 |
| 3 | RolePermissions | 百 | 角色 × 模組權限 |
| 4 | PasswordResetTokens | 百 | 密碼重設 |
| 5 | LoginLogs | 萬 | 登入日誌 |
| 6 | Projects | 十 | 命題專案/梯次 |
| 7 | ProjectPhases | 百 | 專案 × 7 階段時程 |
| 8 | ProjectMembers | 百 | 專案成員指派 |
| 9 | ProjectMemberRoles | 百 | 成員多角色 |
| 10 | ProjectTargets | 百 | 專案題型目標 |
| 11 | MemberQuotas | 百 | 成員命題配額 |
| 12 | QuestionTypes | 7 | 題型定義（Seed） |
| 13 | Questions | 千 | 試題母題 |
| 14 | SubQuestions | 千 | 子題 |
| 15 | QuestionAttributes | 千 | 試題擴充屬性 |
| 16 | QuestionHistoryLogs | 萬 | 試題歷程軌跡 |
| 17 | RevisionReplies | 千 | 修題回覆 |
| 18 | ReviewAssignments | 千 | 審題分配 |
| 19 | ReviewReturnCounts | 百 | 總審退回追蹤 |
| 20 | SimilarityChecks | 千 | 相似度比對 |
| 21 | CannedMessages | 十 | 罐頭訊息 |
| 22 | Teachers | 百 | 教師人才庫 |
| 23 | Announcements | 百 | 系統公告 |
| 24 | UserGuideFiles | 十 | 使用說明手冊 |
| 25 | UrgentReminders | 百 | 急件提醒 |
| 26 | AuditLogs | 萬 | 操作日誌 |
| V1 | DashboardSummaryView | — | 儀表板彙總 |
| V2 | OverdueTasksView | — | 逾期待辦 |
| V3 | QuestionOverviewView | — | 命題總覽（子題展開） |
