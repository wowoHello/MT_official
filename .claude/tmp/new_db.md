# CWT 命題工作平臺 — 資料庫重新設計

> **版本**：v2.0（重新設計版）
> **目標 RDBMS**：Microsoft SQL Server (MSSQL) 2019+
> **設計依據**：`D:\jay_liu\Desktop\命題系統相關\網站功能介紹.md`
> **比對基準**：`D:\MTrefer\db.md`（舊版）
> **核心目標**：解開舊版「Status 一欄塞太多語意」造成階段切換吃力的問題

---

## 第零章 — 分析：舊版痛點與改善方向

### 0.1 舊版 `MT_Questions.Status` 一個欄位身兼五職

舊版 `Status` 是一個 0~12 的 TINYINT，同時負責表達：

| 維度 | 範例值 |
|---|---|
| **工作流程位置** | 0 草稿 → 1 完成 → 2 送審 → 9 採用 |
| **鎖定狀態** | 3/5/7 = 鎖定中、4/6/8 = 開放修題 |
| **三審階段** | 3 互審 / 5 專審 / 7 總審 |
| **修題階段** | 4 互修 / 6 專修 / 8 總修 |
| **終結結果** | 9 採用 / 10 不採用 / 11 結案未採用 / 12 結案入庫 |

一個欄位扛五個維度，**任何一個維度切換都要跨多個 case 寫入**。實際程式中由 `PhaseTransitionCoordinator.EnsurePhaseTransitionAsync` 包成複雜的 switch 迴圈，且為了避免 60 秒去重期間多次跑，前端還要寫 R1/A/B/R2/C 五條規則才能算出正確的「當前狀態 Badge」。

### 0.2 命題總覽的「兩種視覺輸出」其實是兩件事

`網站功能介紹.md` 點出的核心痛點：

| 視覺輸出 | 本質 | 應該的儲存方式 |
|---|---|---|
| **三審狀態軌跡（七階段燈號）** | 題目「走過」哪些階段（歷史軌跡） | 落在獨立的軌跡記錄表 |
| **當前狀態 Badge** | 題目「現在」是什麼狀態（瞬時狀態） | 落在主表的單一 snapshot 欄位 |

舊版兩者都從 `Status` 欄位推導，導致：
- 燈號邏輯要靠 `Status >= 4` 推算「互審完成」、`Status >= 6` 推算「專審完成」⋯
- Badge 文字要靠 `題目 Status × 梯次 PhaseCode × 本人是否送出修題 × 全體審題者是否已給意見` 四維度判定
- 兩個邏輯都依賴同一個 byte，互相耦合，改一邊就會牽動另一邊

### 0.3 其他衍生問題

| 問題 | 影響 |
|---|---|
| `MT_ReviewReturnCounts` 與 `MT_ReviewAssignments` 重複資訊 | 退回次數既能 `COUNT(*)` 也能讀計數欄位，兩者必須同步 |
| `HasReplied` / `AllReviewersResponded` / `IsHistorical` 完全運算式 | 列表頁每次都要 join + group by 算這些 flag |
| 公告 `Status=0/1` 與前端 `DisplayStatus`（草稿/未發佈/已發佈/已下架）不一致 | 排序、篩選邏輯前後端容易飄移 |
| 沒有「決策事件」表 | 三審的歷史紀錄全靠 `MT_ReviewAssignments` 的 Decision + DecidedAt + Comment，無法表達「同一輪審題退回 → 修改 → 再次送審 → 再次退回」的事件鏈 |

### 0.4 改善設計三大原則

1. **正交化**：題目核心狀態拆成 `Lifecycle / DraftStage / CurrentReviewStage / Outcome` 四個獨立欄位，每個欄位只表達一件事
2. **軌跡與 Snapshot 分離**：歷史軌跡用 `MT_QuestionPhaseLog` 永不刪改的事件流；當前畫面顯示用 `Question.DisplayBadge*` snapshot 欄位（由 Service 層維護）
3. **Computed Column 取代前端推導**：公告的 DisplayStatus 等推導值，由 SQL View 推導，前後端讀同一個來源避免飄移

### 0.5 本次改動全清單（vs 舊 db.md）

> 為避免「以為不變但其實偷改」的不誠實情況，下表列出本次設計與舊版的全部差異。
> 每一張表內也都會在標題下方標明「本次改動」說明。

| 表 | 改動類型 | 詳細 |
|---|---|---|
| `MT_QuestionTypes` | 新增欄位 | +`TypeKey VARCHAR(20) UNIQUE`（脫離靠 Id 1~7 對應字串 key 的硬編碼） |
| `MT_Modules` | 型別微調 | `ModuleKey` NVARCHAR(50) → VARCHAR(50) |
| `MT_Roles` | — | 不變 |
| `MT_RolePermissions` | 移除欄位 | −`Permissions TINYINT (0/1 兩級權限)`（前端早已退回 ON/OFF） |
| `MT_Users` | — | 不變 |
| `MT_Teachers` | 型別變更 | `TeacherCode` NVARCHAR→VARCHAR；`IdNumber` NVARCHAR(200)→VARBINARY(256)（強制加密） |
| `MT_PasswordResetTokens` | 型別微調 | `Token` NVARCHAR(500)→VARCHAR(64) |
| `MT_LoginLogs` | 型別+列舉 | `Id` INT→BIGINT；`EventType` 新增 `3=PasswordReset` |
| `MT_AuditLogs` | 型別+列舉 | `Id` INT→BIGINT；`Action` 新增 `3=Restore`；`TargetType` 維持 `TINYINT`（對應字串名稱由 `AuditTargetType` enum 管理） |
| `MT_Projects` | 型別微調 | `ProjectCode` NVARCHAR(20)→VARCHAR(20) |
| `MT_ProjectPhases` | 型別微調 | `SortOrder` INT→TINYINT |
| `MT_ProjectTargets` | **保留欄位** | `Level TINYINT NULL`（保留供未來「題型 × 等級」交叉設定使用）；新增 `(ProjectId, QuestionTypeId, Level)` 唯一約束 |
| `MT_ProjectMembers` | — | 不變 |
| `MT_ProjectMemberRoles` | 新增欄位 | +`IsPrimary BIT`；新增 `(ProjectMemberId, RoleId)` 唯一約束 |
| `MT_MemberQuotas` | **保留欄位** | `Level TINYINT NULL`（同上）；新增 `(ProjectMemberId, QuestionTypeId, Level)` 唯一約束 |
| `MT_Announcements` | 重大變更 | `Status (0/1)` 拆為 `IsDraft BIT`；DisplayStatus 改由 View 計算；新增 `IsDeleted/DeletedAt`；**移除 `ProjectId` 欄位**（綁定梯次拆到 `MT_AnnouncementTargets` 多對多） |
| `MT_AnnouncementTargets` | **新增表** | 公告 × 梯次 多對多關聯（無 row=全站廣播；有 row=指定梯次集合） |
| `MT_Questions` | **核心重構** | Status byte 拆成 `Lifecycle/DraftStage/CurrentReviewStage/Outcome` 四個正交欄位 + `DisplayBadgeText/DisplayBadgeKind/DisplayUpdatedAt` 三個 snapshot 欄位（反正規化警示見 §5.1） + `SubmittedAt/DecidedAt/DeletedBy`；歷史題目改由 `Outcome != 0` 推導，不存 `IsHistorical`；**移除 `GradingNote`**（合併進 `Analysis`） |
| `MT_QuestionPhaseLog` | **新增表** | 七階段燈號的事件流 |
| `MT_QuestionCodeSequence` | — | 不變 |
| `MT_SubQuestions` | 型別微調 | `SortOrder` INT→TINYINT；新增 PK 以外的索引 |
| `MT_QuestionImages` | **新增表** | 題目／子題附圖（題幹、四選項、文章內容），每欄至多 2 張，`FieldType TINYINT` 對應 C# enum |
| `MT_ReviewAssignments` | 重大變更 | −`ReviewStatus` 改用 `RespondedAt IS NULL`；`DecidedAt` 改名為 `RespondedAt`；移除 `Decision/Comment`（改由 `MT_ReviewDecisions` 即時 `OUTER APPLY TOP 1` 取得）；+`Round/UpdatedAt` |
| `MT_ReviewDecisions` | **新增表** | 審題決策事件流（取代舊 `MT_ReviewReturnCounts`） |
| `MT_ReviewReturnCounts` | **整表刪除** | 退回次數改為對 `MT_ReviewDecisions` 即時 COUNT |
| `MT_RevisionReplies` | 新增欄位 | +`Round TINYINT`（本輪由 `MAX(Round)` 推導，不另存 `IsCurrentRound` flag） |
| `MT_SimilarityChecks` | — | 不變 |
| `MT_Notifications` | **新增表** | 站內通知（鈴鐺與留言預留） |
| `MT_UserGuideFiles` | 新增欄位 | +`CreatedAt`（方便依時間排序） |

> **若有任一改動你不接受，請直接點名（例：「ProjectTargets 的 Level 不要拿掉」），我會把那一條改回去**。

---

## 第一章 — 命名與型別約定

| 規則 | 說明 |
|---|---|
| 表名前綴 | 一律 `MT_`（與舊版相容） |
| 主鍵 | `Id INT IDENTITY(1,1) PRIMARY KEY`（事件流類型用 `BIGINT`） |
| 時間戳 | `CreatedAt / UpdatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME()` |
| 軟刪除 | `IsDeleted BIT NOT NULL DEFAULT 0` + `DeletedAt DATETIME2 NULL` |
| 列舉值 | `TINYINT`（0~255）優先 |
| 布林 | `BIT`（不用 0/1 TINYINT 模擬） |
| 文字 | UI 顯示用 `NVARCHAR`，固定編碼如 ProjectCode 用 `VARCHAR` |
| 富文本 | `NVARCHAR(MAX)`（Quill HTML） |
| 雜湊 | `BINARY(32)`（SHA2_256） |
| 唯一索引 | `UQ_<TableName>_<Columns>` |
| 一般索引 | `IX_<TableName>_<Columns>` |

---

## 第二章 — 字典與基礎表

### 2.1 `MT_QuestionTypes` — 七種題型字典

> **本次改動**：新增 `TypeKey VARCHAR(20) UNIQUE` 欄位，存放前端使用的字串 key（single / select ⋯）。
> 舊版只靠固定 `Id 1~7` 對應 QuestionTypeCodes，種子需要 `SET IDENTITY_INSERT` 強制鎖定，
> 一旦資料表跨環境重建容易跑掉。新增 TypeKey 後外部 join 改用字串對應，不再依賴 Id 順序。

```sql
CREATE TABLE dbo.MT_QuestionTypes (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    TypeKey     VARCHAR(20) NOT NULL UNIQUE,   -- ★ 新增：single / select / readGroup ⋯
    Name        NVARCHAR(50) NOT NULL,
    Icon        NVARCHAR(50),
    SortOrder   INT NOT NULL DEFAULT 0
);
-- 種子：1=single, 2=select, 3=readGroup, 4=longText, 5=shortGroup, 6=listen, 7=listenGroup
```

### 2.2 `MT_Modules` — 功能模組字典

> **本次改動**：`ModuleKey` 由 `NVARCHAR(50)` 改為 `VARCHAR(50)`（純 ASCII 字串如 dashboard / projects，省一半空間且 join 比對更快）。其餘欄位不變。

```sql
CREATE TABLE dbo.MT_Modules (
    Id            INT IDENTITY(1,1) PRIMARY KEY,
    ModuleKey     VARCHAR(50) NOT NULL UNIQUE,    -- ★ 由 NVARCHAR(50) 改為 VARCHAR(50)
    Name          NVARCHAR(50) NOT NULL,
    Icon          NVARCHAR(50),
    PageUrl       NVARCHAR(100),
    SortOrder     INT NOT NULL DEFAULT 0,
    IsActive      BIT NOT NULL DEFAULT 1,
    Description   NVARCHAR(500),
    ColorClass    NVARCHAR(50),
    BgColorClass  NVARCHAR(50)
);
```

### 2.3 `MT_Roles` — 角色字典（與舊版一致）

```sql
CREATE TABLE dbo.MT_Roles (
    Id           INT IDENTITY(1,1) PRIMARY KEY,
    Name         NVARCHAR(50) NOT NULL UNIQUE,
    Category     TINYINT NOT NULL,            -- 0=內部, 1=外部
    Description  NVARCHAR(500),
    IsDefault    BIT NOT NULL DEFAULT 0,      -- 預設角色不可改權限、不可刪除
    CreatedAt    DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    UpdatedAt    DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);
```

### 2.4 `MT_RolePermissions` — 角色 × 模組權限（簡化為純 ON/OFF）

> **改動**：拿掉舊版 `Permissions TINYINT (0=檢視, 1=編輯)` 兩級權限欄位，改為單純 `IsEnabled BIT`，與實際程式碼 `RolePermissionInput` 一致。

```sql
CREATE TABLE dbo.MT_RolePermissions (
    Id         INT IDENTITY(1,1) PRIMARY KEY,
    RoleId     INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Roles(Id),
    ModuleId   INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Modules(Id),
    IsEnabled  BIT NOT NULL DEFAULT 0,
    CreatedAt  DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    UpdatedAt  DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    CONSTRAINT UQ_MT_RolePermissions_RoleId_ModuleId UNIQUE (RoleId, ModuleId)
);
```

---

## 第三章 — 使用者與身分

### 3.1 `MT_Users` — 系統帳號（內部與外部共用，與舊版完全相同）

```sql
CREATE TABLE dbo.MT_Users (
    Id              INT IDENTITY(1,1) PRIMARY KEY,
    Username        NVARCHAR(100) NOT NULL,        -- 內部=帳號；外部=Email
    DisplayName     NVARCHAR(50) NOT NULL,
    Email           NVARCHAR(200),
    PasswordHash    BINARY(32) NOT NULL,           -- SHA2_256
    RoleId          INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Roles(Id),
    Status          TINYINT NOT NULL DEFAULT 1,    -- 0=停用, 1=啟用
    CompanyTitle    NVARCHAR(100),                 -- 內部同仁職稱（外部=NULL）
    Note            NVARCHAR(500),
    IsFirstLogin    BIT NOT NULL DEFAULT 1,        -- 強制首次改密碼
    LastLoginAt     DATETIME2,
    LockoutUntil    DATETIME2,                     -- 30 分鐘 3 次錯誤鎖 15 分鐘
    CreatedAt       DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    UpdatedAt       DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);

-- 過濾唯一索引：Username/Email 非空且非空字串才唯一
CREATE UNIQUE INDEX UX_MT_Users_Username
    ON dbo.MT_Users(Username) WHERE Username IS NOT NULL AND Username <> '';
CREATE UNIQUE INDEX UX_MT_Users_Email
    ON dbo.MT_Users(Email) WHERE Email IS NOT NULL AND Email <> '';
```

### 3.2 `MT_Teachers` — 教師人才庫延伸資料（1:1 對應 Users）

> **本次改動**：
> - `TeacherCode` 由 `NVARCHAR(10)` 改為 `VARCHAR(10)`（純 ASCII 編碼）
> - `IdNumber` 由 `NVARCHAR(200)`（明文遮罩）改為 `VARBINARY(256)`（強制加密儲存，符合個資保護），讀取時由服務層解密
> - 其餘欄位保持與舊版一致

```sql
CREATE TABLE dbo.MT_Teachers (
    Id              INT IDENTITY(1,1) PRIMARY KEY,
    UserId          INT NOT NULL UNIQUE FOREIGN KEY REFERENCES dbo.MT_Users(Id),
    TeacherCode     VARCHAR(10) NOT NULL UNIQUE,   -- ★ 由 NVARCHAR(10) 改為 VARCHAR(10)
    Gender          TINYINT,                       -- 0=未知, 1=男, 2=女
    Phone           NVARCHAR(20),
    IdNumber        VARBINARY(256),                -- ★ 由 NVARCHAR(200) 改為 VARBINARY(256)（強制加密）
    School          NVARCHAR(100) NOT NULL,
    Department      NVARCHAR(50),
    Title           NVARCHAR(20),
    Expertise       NVARCHAR(200),
    TeachingYears   INT,
    Education       TINYINT,                       -- 1=學士, 2=碩士, 3=博士
    Note            NVARCHAR(500),
    CreatedAt       DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    UpdatedAt       DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);
```

### 3.3 `MT_PasswordResetTokens` — 忘記密碼 Token（10 分鐘 TTL）

> **本次改動**：`Token` 由 `NVARCHAR(500)` 改為 `VARCHAR(64)`（GUID 字串為純 ASCII 且最長 36 字元，500 太誇張）。其餘欄位不變。

```sql
CREATE TABLE dbo.MT_PasswordResetTokens (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    UserId      INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Users(Id),
    Token       VARCHAR(64) NOT NULL UNIQUE,        -- ★ 由 NVARCHAR(500) 改為 VARCHAR(64)
    RequestIp   NVARCHAR(50),
    ExpiresAt   DATETIME2 NOT NULL,
    IsUsed      BIT NOT NULL DEFAULT 0,
    CreatedAt   DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);

CREATE INDEX IX_MT_PasswordResetTokens_UserId ON dbo.MT_PasswordResetTokens(UserId);
```

---

## 第四章 — 專案梯次與時程

### 4.1 `MT_Projects` — 梯次主表

> **本次改動**：`ProjectCode` 由 `NVARCHAR(20)` 改為 `VARCHAR(20)`（純 ASCII 編碼）。其餘欄位不變。
>
> **設計重點（沿用舊版）**：專案狀態（準備中 / 進行中 / 已結案）由 `StartDate / EndDate / ClosedAt` 推導，不另存 `Status` 欄位，避免狀態與日期不同步。

```sql
CREATE TABLE dbo.MT_Projects (
    Id           INT IDENTITY(1,1) PRIMARY KEY,
    ProjectCode  VARCHAR(20) NOT NULL UNIQUE,        -- ★ 由 NVARCHAR(20) 改為 VARCHAR(20)
    Name         NVARCHAR(100) NOT NULL,
    Year         INT NOT NULL,                       -- 影響 QuestionCode 流水
    School       NVARCHAR(100),                      -- NULL = 自辦梯次
    StartDate    DATE NOT NULL,                      -- 產學區間起
    EndDate      DATE NOT NULL,                      -- 產學區間迄
    ClosedAt     DATETIME2,                          -- 實際結案時間（NULL=未結案）
    CreatedBy    INT FOREIGN KEY REFERENCES dbo.MT_Users(Id),
    Description  NVARCHAR(500),
    IsDeleted    BIT NOT NULL DEFAULT 0,
    DeletedAt    DATETIME2,
    CreatedAt    DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    UpdatedAt    DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);
```

### 4.2 `MT_ProjectPhases` — 八階段時程

> **本次改動**：`SortOrder` 由 `INT` 改為 `TINYINT`（最多 8 個階段，TINYINT 已足夠）。其餘欄位不變。
>
> **設計重點**：PhaseCode 是整個資料庫最重要的對齊鍵，影響題目顯示、修題鎖定、總召退回計次邏輯。

```sql
CREATE TABLE dbo.MT_ProjectPhases (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    ProjectId   INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Projects(Id),
    PhaseCode   TINYINT NOT NULL,
    -- 1=產學區間（umbrella）
    -- 2=命題階段
    -- 3=交互審題（互審）
    -- 4=互審修題（互修）
    -- 5=專家審題（專審）
    -- 6=專審修題（專修）
    -- 7=總召審題（總審）
    -- 8=總召修題（總修）
    PhaseName   NVARCHAR(50) NOT NULL,
    StartDate   DATE NOT NULL,
    EndDate     DATE NOT NULL,
    SortOrder   TINYINT NOT NULL,    -- ★ 由 INT 改為 TINYINT
    CreatedAt   DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    UpdatedAt   DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    CONSTRAINT UQ_MT_ProjectPhases_ProjectId_PhaseCode UNIQUE (ProjectId, PhaseCode)
);

CREATE INDEX IX_MT_ProjectPhases_ProjectId_StartDate ON dbo.MT_ProjectPhases(ProjectId, StartDate);
```

### 4.3 `MT_ProjectTargets` — 七種題型目標題數

> **本次改動**：
> - **保留舊版的 `Level TINYINT NULL` 欄位**（沿用舊版設計「等級 (配合前端若無區分則為 NULL)」，預留未來「題型 × 等級」交叉設定）
> - **新增 `(ProjectId, QuestionTypeId, Level)` 唯一約束**（舊版無唯一約束，這裡防呆；SQL Server 會把多個 NULL 視為相等，因此「不分等級」的情境每個題型仍只允許一筆 Level=NULL 紀錄）
>
> 現行 UI（命題專案管理 SlideOver）只設定「題型總題數」，所以實務上會以 `Level=NULL` 寫入；未來若要分等級，直接寫入 `Level=0/1/2...` 即可，不需改結構。

```sql
CREATE TABLE dbo.MT_ProjectTargets (
    Id              INT IDENTITY(1,1) PRIMARY KEY,
    ProjectId       INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Projects(Id),
    QuestionTypeId  INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_QuestionTypes(Id),
    Level           TINYINT NULL,                    -- ★ 保留：等級（不分等級時 = NULL）
    TargetCount     INT NOT NULL DEFAULT 0,
    CONSTRAINT UQ_MT_ProjectTargets_Project_Type_Level UNIQUE (ProjectId, QuestionTypeId, Level)  -- ★ 新增
);
```

### 4.4 `MT_ProjectMembers` — 專案參與成員（與舊版完全相同）

```sql
CREATE TABLE dbo.MT_ProjectMembers (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    ProjectId   INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Projects(Id),
    UserId      INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Users(Id),
    JoinedAt    DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    CONSTRAINT UQ_MT_ProjectMembers_Project_User UNIQUE (ProjectId, UserId)
);
```

### 4.5 `MT_ProjectMemberRoles` — 一人多身分

> **本次改動**：
> - **新增 `IsPrimary BIT` 欄位**（每位成員恰一筆 IsPrimary=1，對齊 `網站功能介紹.md` 中「主要身份 + 多個附加身份」的 UI 設計）
> - **新增 `(ProjectMemberId, RoleId)` 唯一約束**（舊版可重複插入相同身分，這裡防呆）

```sql
CREATE TABLE dbo.MT_ProjectMemberRoles (
    Id                INT IDENTITY(1,1) PRIMARY KEY,
    ProjectMemberId   INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_ProjectMembers(Id),
    RoleId            INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Roles(Id),
    IsPrimary         BIT NOT NULL DEFAULT 0,         -- ★ 新增：主要身分（每位成員恰一筆 IsPrimary=1）
    CONSTRAINT UQ_MT_ProjectMemberRoles_Member_Role UNIQUE (ProjectMemberId, RoleId)  -- ★ 新增唯一約束
);
```

### 4.6 `MT_MemberQuotas` — 命題教師的七種題型配額

> **本次改動**：
> - **保留舊版的 `Level TINYINT NULL` 欄位**（同 4.3，預留未來「題型 × 等級」配額分配）
> - **新增 `(ProjectMemberId, QuestionTypeId, Level)` 唯一約束**
>
> 現行 UI 配額分配只看「題型總題數」，所以以 `Level=NULL` 寫入；未來若要分等級配額直接寫入 `Level` 即可。

```sql
CREATE TABLE dbo.MT_MemberQuotas (
    Id                INT IDENTITY(1,1) PRIMARY KEY,
    ProjectMemberId   INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_ProjectMembers(Id),
    QuestionTypeId    INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_QuestionTypes(Id),
    Level             TINYINT NULL,                  -- ★ 保留：等級（不分等級時 = NULL）
    QuotaCount        INT NOT NULL DEFAULT 0,
    CONSTRAINT UQ_MT_MemberQuotas_Member_Type_Level UNIQUE (ProjectMemberId, QuestionTypeId, Level)  -- ★ 新增
);
```

---

## 第五章 — 題目主檔（核心改善區）

### 5.1 `MT_Questions` — 題目主表（**關鍵：狀態欄位正交拆分**）

> **這是本次重構最重要的改動。**
>
> 把舊版 `Status` 一個欄位塞五個維度的設計拆成 **5 個正交欄位 + 2 個 Snapshot 欄位**。
> 每個欄位只負責一件事，UI 直接讀對應欄位即可，**不必再寫 R1/A/B/R2/C 五條規則**。

```sql
CREATE TABLE dbo.MT_Questions (
    Id              INT IDENTITY(1,1) PRIMARY KEY,
    ProjectId       INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Projects(Id),
    QuestionTypeId  INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_QuestionTypes(Id),
    QuestionCode    VARCHAR(30) NOT NULL UNIQUE,    -- Q-{Year}-{NNNNN}
    CreatorId       INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Users(Id),

    -- ═══════════════════════════════════════════════════════════════
    -- ★ 核心狀態：5 個正交欄位（取代舊版單一 Status byte）
    -- ═══════════════════════════════════════════════════════════════

    -- ① 生命週期：題目目前停留在哪個大階段
    Lifecycle       TINYINT NOT NULL DEFAULT 0,
    -- 0 = Draft       命題中（草稿 / 完成 / 已送審）
    -- 1 = InReview    審題鎖定中
    -- 2 = InRevision  修題開放中
    -- 3 = Decided     已最終決策

    -- ② 命題完成度：在 Draft 階段內細分
    DraftStage      TINYINT NOT NULL DEFAULT 0,
    -- 0 = 草稿（尚未按命題完成）
    -- 1 = 命題完成（可送審）
    -- 2 = 已送審（鎖定，等待審題分配）
    -- ※ 僅 Lifecycle=0 (Draft) 時有意義

    -- ③ 當前審題階段：題目目前處於哪一審
    CurrentReviewStage  TINYINT NOT NULL DEFAULT 0,
    -- 0 = None        尚未進入審題
    -- 1 = Peer        互審
    -- 2 = Expert      專審
    -- 3 = Final       總審
    -- ※ Lifecycle=1 (InReview) 或 Lifecycle=2 (InRevision) 時必填
    --   修題階段時表「上一輪是哪一審退的」

    -- ④ 最終結果：Decided 後永久落地
    Outcome         TINYINT NOT NULL DEFAULT 0,
    -- 0 = Pending          尚未決議
    -- 1 = Adopted          採用（含結案入庫）
    -- 2 = Rejected         不採用（總審判定）
    -- 3 = ClosedNotAdopted 結案時非採用→歸為不採用入庫

    -- ※ 「歷史題目」由 `Outcome != 0` 推導（已決策），或 JOIN MT_Projects 看 `ClosedAt IS NOT NULL`
    --   不存 IsHistorical flag，避免結案時的批次同步飄移風險

    -- ═══════════════════════════════════════════════════════════════
    -- ★ Display Snapshot：直接給列表頁與儀表板讀
    --   由服務層在每次跨階段事件後同步寫入，省掉前端推導
    -- ═══════════════════════════════════════════════════════════════

    DisplayBadgeText    NVARCHAR(20),
    -- 「待審」/「已給意見」/「修題已送出」/「未完成命題」/「採用」⋯
    DisplayBadgeKind    TINYINT,
    -- 0=default, 1=info(藍), 2=success(綠), 3=warning(橘), 4=danger(紅)
    DisplayUpdatedAt    DATETIME2,

    -- ═══════════════════════════════════════════════════════════════
    -- 屬性側邊欄欄位（與舊版相同，編碼參考舊 db.md 第 5 章）
    -- ═══════════════════════════════════════════════════════════════
    Level               TINYINT,
    Difficulty          TINYINT,
    Topic               TINYINT,
    Subtopic            TINYINT,
    Genre               TINYINT,
    Material            TINYINT,
    WritingMode         TINYINT,
    AudioType           TINYINT,
    CoreAbility         TINYINT,
    DetailIndicator     TINYINT,

    -- ═══════════════════════════════════════════════════════════════
    -- 內容欄位
    -- ═══════════════════════════════════════════════════════════════
    Stem                NVARCHAR(MAX),
    Analysis            NVARCHAR(MAX),
    CorrectAnswer       VARCHAR(10),
    OptionA             NVARCHAR(MAX),
    OptionB             NVARCHAR(MAX),
    OptionC             NVARCHAR(MAX),
    OptionD             NVARCHAR(MAX),
    ArticleTitle        NVARCHAR(MAX),
    ArticleContent      NVARCHAR(MAX),
    AudioUrl            NVARCHAR(500),
    -- 註：舊版 GradingNote（批閱說明）已合併進 Analysis；長文題型 UI label 顯示「批閱說明」、其餘題型顯示「試題解析」，但讀寫同一欄位

    -- ═══════════════════════════════════════════════════════════════
    -- 軟刪除與時間
    -- ═══════════════════════════════════════════════════════════════
    IsDeleted    BIT NOT NULL DEFAULT 0,
    DeletedAt    DATETIME2,
    DeletedBy    INT FOREIGN KEY REFERENCES dbo.MT_Users(Id),
    SubmittedAt  DATETIME2,        -- 命題送審時間（取代靠 Status=2 反查）
    DecidedAt    DATETIME2,        -- 最終決策時間（Outcome 落定時間）
    CreatedAt    DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    UpdatedAt    DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);

-- 索引
CREATE INDEX IX_MT_Questions_ProjectId_Lifecycle ON dbo.MT_Questions(ProjectId, Lifecycle);
CREATE INDEX IX_MT_Questions_CreatorId           ON dbo.MT_Questions(CreatorId);
CREATE INDEX IX_MT_Questions_Outcome             ON dbo.MT_Questions(Outcome) WHERE Outcome > 0;
CREATE INDEX IX_MT_Questions_Project_Active      ON dbo.MT_Questions(ProjectId, Lifecycle) WHERE IsDeleted = 0;
```

> ⚠️ **反正規化警示（DisplayBadgeText / DisplayBadgeKind / DisplayUpdatedAt）**
>
> 這三個欄位屬於 **Precomputed Column 反正規化**：原本可由 `Lifecycle + DraftStage + CurrentReviewStage + Outcome + 本人本輪修題狀態 + 本輪審題者狀態` 在讀取時推導，但因列表頁與儀表板高頻讀取、推導邏輯複雜（涉及多表狀態），實體化儲存以換取讀取效能。為避免退化成「飄移的快取」，**必須遵守以下三條配套規範**：
>
> 1. **單一寫入點**：所有會改 Badge 的事件必須走同一個 Service 方法（建議 `QuestionStateService.RefreshDisplayBadgeAsync(questionId)`）。任何 `UPDATE MT_Questions SET DraftStage/Lifecycle/CurrentReviewStage/Outcome = ...` 的 SQL **不得直接寫**，一律透過該 Service。寫入時機涵蓋：命題送審 / 草稿 / 完成、梯次階段切換、審題決策寫入、修題回覆送出、結案入庫。
> 2. **重算工具**：必須提供批次重算方法 `RecalculateAllBadgesAsync(int projectId)`，從原始狀態欄位重新計算整個梯次的 Badge。萬一某個寫入點漏寫、或邏輯升級時可一鍵刷新，作為飄移的安全網。
> 3. **計算邏輯集中**：Badge 推導邏輯只能寫在一個地方（建議 `Models/QuestionDisplayBadgeCalculator.cs`），輸入是 Question entity + 上下文（本人 UserId、本輪修題狀態），輸出 `(text, kind)`。Service 寫入、重算工具、若有任何後台維護腳本都呼叫這個 Calculator，**禁止散在各 Service / View 重複實作**。

### 5.2 從舊 Status 對應到新欄位的對照表

| 舊 Status | 新 Lifecycle | 新 DraftStage | 新 CurrentReviewStage | 新 Outcome |
|---|---|---|---|---|
| 0 命題草稿 | 0 Draft | 0 | 0 | 0 |
| 1 命題完成 | 0 Draft | 1 | 0 | 0 |
| 2 命題送審 | 0 Draft | 2 | 0 | 0 |
| 3 互審中 | 1 InReview | — | 1 Peer | 0 |
| 4 互審修題中 | 2 InRevision | — | 1 Peer | 0 |
| 5 專審中 | 1 InReview | — | 2 Expert | 0 |
| 6 專審修題中 | 2 InRevision | — | 2 Expert | 0 |
| 7 總審中 | 1 InReview | — | 3 Final | 0 |
| 8 總審修題中 | 2 InRevision | — | 3 Final | 0 |
| 9 採用 | 3 Decided | — | — | 1 Adopted |
| 10 不採用 | 3 Decided | — | — | 2 Rejected |
| 11 結案未採用 | 3 Decided | — | — | 3 ClosedNotAdopted |
| 12 結案入庫 | 3 Decided | — | — | 1 Adopted |

> **設計效益**：
> - 「鎖定中」直接看 `Lifecycle = 1`，不必判斷 `Status IN (3,5,7)`
> - 「採用題目」直接看 `Outcome IN (1)`，不必判斷 `Status IN (9, 12)`
> - 「修題開放」直接看 `Lifecycle = 2`，不必判斷 `Status IN (4,6,8)`
> - 命題教師作品數、配額計算、儀表板統計都簡化為單欄條件

### 5.3 `MT_QuestionPhaseLog` — **七階段軌跡記錄表（新增）**

> **這是專為「命題總覽」的七階段燈號設計的事件流表。**
>
> 每當題目進入或離開某個階段就插入一筆，**永不修改、永不刪除**（軟刪除題目時這張表也保留）。
> 七階段燈號只要 `SELECT PhaseCode FROM MT_QuestionPhaseLog WHERE QuestionId = X` 即可，**不必再從 Status 推算**。

```sql
CREATE TABLE dbo.MT_QuestionPhaseLog (
    Id            BIGINT IDENTITY(1,1) PRIMARY KEY,
    QuestionId    INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Questions(Id),
    PhaseCode     TINYINT NOT NULL,
    -- 對應 MT_ProjectPhases.PhaseCode（2~8）
    -- 2=命題, 3=互審, 4=互修, 5=專審, 6=專修, 7=總審, 8=總修
    EventType     TINYINT NOT NULL,
    -- 1 = Entered    題目進入此階段
    -- 2 = Completed  此階段對該題完成（離開）
    -- 3 = Skipped    跳過（總審第 3 次解鎖總召自改 → 跳過命題教師修題）
    OccurredAt    DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    TriggeredBy   INT FOREIGN KEY REFERENCES dbo.MT_Users(Id),
    Note          NVARCHAR(200)
);

CREATE INDEX IX_MT_QuestionPhaseLog_QuestionId_PhaseCode ON dbo.MT_QuestionPhaseLog(QuestionId, PhaseCode);
CREATE INDEX IX_MT_QuestionPhaseLog_QuestionId_OccurredAt ON dbo.MT_QuestionPhaseLog(QuestionId, OccurredAt);
```

**七階段燈號渲染邏輯**：

```sql
-- PhaseProgressStepper 一次取一題的軌跡
SELECT PhaseCode,
       MIN(CASE WHEN EventType = 1 THEN OccurredAt END) AS EnteredAt,
       MAX(CASE WHEN EventType = 2 THEN OccurredAt END) AS CompletedAt
FROM dbo.MT_QuestionPhaseLog
WHERE QuestionId = @QuestionId
GROUP BY PhaseCode
ORDER BY PhaseCode;
```

每一顆球的狀態：
- `EnteredAt IS NOT NULL AND CompletedAt IS NULL` → 進行中（藍色）
- `CompletedAt IS NOT NULL` → 已完成（綠勾）
- `CompletedAt IS NULL AND EnteredAt IS NULL` → 未到（灰色）
- 修題階段「雙態」：藍勾 + Outcome 對應 Badge

### 5.4 `MT_QuestionCodeSequence` — 年度流水號（不變）

```sql
CREATE TABLE dbo.MT_QuestionCodeSequence (
    Year       INT NOT NULL PRIMARY KEY,
    NextValue  INT NOT NULL DEFAULT 1
);
```

### 5.5 `MT_SubQuestions` — 題組子題

> **本次改動**：
> - `SortOrder` 由 `INT NOT NULL DEFAULT 1` 改為 `TINYINT NOT NULL DEFAULT 1`（子題數最多 ~10 題，TINYINT 足夠且省空間）
> - 新增 `IX_MT_SubQuestions_ParentQuestionId` 索引（舊版只能靠 PK 全掃描）
> - 欄位語意與編碼規則沿用舊 db.md 第 5.5 節，不重複文檔

```sql
CREATE TABLE dbo.MT_SubQuestions (
    Id                INT IDENTITY(1,1) PRIMARY KEY,
    ParentQuestionId  INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Questions(Id),
    SortOrder         TINYINT NOT NULL DEFAULT 1,    -- ★ 由 INT 改為 TINYINT
    Stem              NVARCHAR(MAX),
    CorrectAnswer     VARCHAR(10),
    OptionA           NVARCHAR(MAX),
    OptionB           NVARCHAR(MAX),
    OptionC           NVARCHAR(MAX),
    OptionD           NVARCHAR(MAX),
    Analysis          NVARCHAR(MAX),
    CoreAbility       TINYINT,    -- 編碼依母題 QuestionTypeId 解碼（沿用舊 db.md）
    Indicator         TINYINT,    -- 編碼依母題 QuestionTypeId 解碼
    FixedDifficulty   TINYINT     -- ListenGroup 子題固定難度
);

CREATE INDEX IX_MT_SubQuestions_ParentQuestionId ON dbo.MT_SubQuestions(ParentQuestionId);
```

### 5.6 `MT_QuestionImages` — **題目／子題附圖（新增表）**

> **設計動機**：原本圖片是直接內嵌進 Quill 編輯器的 HTML 中，導致文字與圖片耦合、難以後續處理（例如題庫檢索、跨平台輸出、單獨更換素材）。新版將「文字」與「圖片」分離，文字仍存於 `Stem`/`OptionA-D`/`Analysis`/`ArticleContent` 等欄位，圖片改由本表獨立管理。
>
> **設計重點**：
> - **每欄至多 2 張**：由前端表單限制，DB 不額外加 `CHECK COUNT` 約束（避免插入時觸發子查詢成本）
> - **FieldType 用 TINYINT** 對應 C# enum `QuestionImageField`（符合 CLAUDE.md「能存數字不存文字」原則）
> - **QuestionId / SubQuestionId 互斥**：一張圖只屬於母題或子題，由 `CK_MT_QuestionImages_OneParent` 約束擋掉同時指向兩者或都不指向
> - **子題不允許 FieldType=6 (ArticleContent)**：因為 `ArticleContent` 是母題層級欄位，子題沒這個概念，由 `CK_MT_QuestionImages_SubNoArticle` 擋掉

```sql
CREATE TABLE dbo.MT_QuestionImages (
    Id              INT IDENTITY(1,1) PRIMARY KEY,
    QuestionId      INT NULL FOREIGN KEY REFERENCES dbo.MT_Questions(Id),
    SubQuestionId   INT NULL FOREIGN KEY REFERENCES dbo.MT_SubQuestions(Id),
    FieldType       TINYINT NOT NULL,
    -- 1 = Stem            題幹
    -- 2 = OptionA
    -- 3 = OptionB
    -- 4 = OptionC
    -- 5 = OptionD
    -- 6 = ArticleContent  文章內容（僅母題 MT_Questions 有效）
    -- 對應 C# enum QuestionImageField（建議放 Models/QuestionImageField.cs）
    ImagePath       NVARCHAR(500) NOT NULL,        -- 例：/uploads/{guid}.png（沿用既有 POST /api/upload 機制）
    SortOrder       TINYINT NOT NULL DEFAULT 1,    -- 1 或 2（同欄位多張時的顯示順序）
    CreatedAt       DATETIME2 NOT NULL DEFAULT SYSDATETIME(),

    -- 一張圖只能屬於母題或子題其中一者
    CONSTRAINT CK_MT_QuestionImages_OneParent CHECK (
        (QuestionId IS NOT NULL AND SubQuestionId IS NULL) OR
        (QuestionId IS NULL AND SubQuestionId IS NOT NULL)
    ),
    -- 子題沒有 ArticleContent 欄位，禁止 FieldType=6 配 SubQuestionId
    CONSTRAINT CK_MT_QuestionImages_SubNoArticle CHECK (
        SubQuestionId IS NULL OR FieldType <> 6
    )
);

CREATE INDEX IX_MT_QuestionImages_Question_Field ON dbo.MT_QuestionImages(QuestionId, FieldType) WHERE QuestionId IS NOT NULL;
CREATE INDEX IX_MT_QuestionImages_SubQuestion_Field ON dbo.MT_QuestionImages(SubQuestionId, FieldType) WHERE SubQuestionId IS NOT NULL;
```

**典型查詢範例**：

```sql
-- 取某題（母題）的所有附圖，依欄位 + SortOrder 排序
SELECT FieldType, ImagePath, SortOrder
FROM dbo.MT_QuestionImages
WHERE QuestionId = @qid
ORDER BY FieldType, SortOrder;

-- 取題組母題 + 所有子題的附圖（一次撈完，前端依 FieldType / SortOrder 分組顯示）
SELECT 'Q' AS Source, q.Id AS OwnerId, i.FieldType, i.ImagePath, i.SortOrder
FROM dbo.MT_QuestionImages i
JOIN dbo.MT_Questions q ON q.Id = i.QuestionId
WHERE q.Id = @qid
UNION ALL
SELECT 'S', s.Id, i.FieldType, i.ImagePath, i.SortOrder
FROM dbo.MT_QuestionImages i
JOIN dbo.MT_SubQuestions s ON s.Id = i.SubQuestionId
WHERE s.ParentQuestionId = @qid
ORDER BY Source, OwnerId, FieldType, SortOrder;
```

> **C# enum 對應建議**（放 `Models/QuestionImageField.cs`）：
>
> ```csharp
> public enum QuestionImageField : byte
> {
>     Stem           = 1,
>     OptionA        = 2,
>     OptionB        = 3,
>     OptionC        = 4,
>     OptionD        = 5,
>     ArticleContent = 6,    // 僅 MT_Questions 有效
> }
> ```

---

## 第六章 — 審題與決策（核心改善區）

### 6.1 `MT_ReviewAssignments` — 審題指派

> **改動**：
> - 移除 `ReviewStatus` 欄位（待審 / 審核中 / 已完成），改為由 `RespondedAt` 欄位的 NULL/有值判斷
> - **不存 `LatestDecision / LatestComment` 快取欄位**：最新決策一律由 `MT_ReviewDecisions` 即時 `OUTER APPLY TOP 1 ORDER BY DecidedAt DESC` 取得，避免 Assignments 與 Decisions 兩處同步飄移

```sql
CREATE TABLE dbo.MT_ReviewAssignments (
    Id              INT IDENTITY(1,1) PRIMARY KEY,
    QuestionId      INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Questions(Id),
    ProjectId       INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Projects(Id),
    ReviewerId      INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Users(Id),
    ReviewStage     TINYINT NOT NULL,            -- 1=Peer, 2=Expert, 3=Final
    Round           TINYINT NOT NULL DEFAULT 1,  -- 第幾輪（總審支援 1~3 輪）

    -- 完成狀態（取代舊 ReviewStatus）：RespondedAt 為 NULL = 待審；有值 = 已決策
    -- 最新決策內容（Decision / Comment）改由 OUTER APPLY MT_ReviewDecisions 取得，本表不存
    RespondedAt     DATETIME2,

    CreatedAt       DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    UpdatedAt       DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    CONSTRAINT UQ_MT_ReviewAssignments_Q_R_S_R UNIQUE (QuestionId, ReviewerId, ReviewStage, Round)
);

CREATE INDEX IX_MT_ReviewAssignments_Reviewer_Stage ON dbo.MT_ReviewAssignments(ReviewerId, ReviewStage, RespondedAt);
CREATE INDEX IX_MT_ReviewAssignments_Question_Stage ON dbo.MT_ReviewAssignments(QuestionId, ReviewStage);
```

**列表頁取得「指派 + 最新決策」**（取代舊 `LatestDecision / LatestComment` 欄位）：

```sql
SELECT a.Id, a.QuestionId, a.ReviewerId, a.ReviewStage, a.Round, a.RespondedAt,
       d.Decision, d.Comment, d.DecidedAt
FROM dbo.MT_ReviewAssignments a
OUTER APPLY (
    SELECT TOP 1 Decision, Comment, DecidedAt
    FROM dbo.MT_ReviewDecisions
    WHERE AssignmentId = a.Id
    ORDER BY DecidedAt DESC
) d
WHERE a.ReviewerId = @me;
-- 配合 IX_MT_ReviewDecisions_QuestionId_DecidedAt（§6.2）為單筆 lookup
```

### 6.2 `MT_ReviewDecisions` — **審題決策事件流（新增）**

> **這張表取代舊版 `MT_ReviewReturnCounts`**。
>
> 每次審題人按下「採用 / 改後再審 / 不採用」就插入一筆事件。退回次數不再另存欄位，**直接 `COUNT(*)` 即可**。
> 同時也作為 `ReviewHistoryTimeline` 元件的歷程資料來源。

```sql
CREATE TABLE dbo.MT_ReviewDecisions (
    Id              BIGINT IDENTITY(1,1) PRIMARY KEY,
    AssignmentId    INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_ReviewAssignments(Id),
    QuestionId      INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Questions(Id),
    ReviewerId      INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Users(Id),
    ReviewStage     TINYINT NOT NULL,    -- 1=Peer, 2=Expert, 3=Final
    Round           TINYINT NOT NULL,    -- 該題在該階段的第幾輪
    Decision        TINYINT NOT NULL,    -- 0=Comment, 1=Approve, 2=Revise, 3=Reject
    Comment         NVARCHAR(MAX),
    DecidedAt       DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);

CREATE INDEX IX_MT_ReviewDecisions_QuestionId_DecidedAt ON dbo.MT_ReviewDecisions(QuestionId, DecidedAt);
CREATE INDEX IX_MT_ReviewDecisions_Question_Stage_Decision ON dbo.MT_ReviewDecisions(QuestionId, ReviewStage, Decision);
```

**退回次數查詢**（取代舊 `MT_ReviewReturnCounts.ReturnCount`）：

```sql
-- 某題在總審階段被退回幾次
SELECT COUNT(*) AS ReturnCount
FROM dbo.MT_ReviewDecisions
WHERE QuestionId = @qid
  AND ReviewStage = 3       -- Final
  AND Decision IN (2, 3);   -- Revise or Reject
```

**第 3 次解鎖總召自改**（取代舊 `CanEditByReviewer` 欄位）：

```sql
-- 計算退回次數 >= 2 即解鎖
SELECT CASE WHEN COUNT(*) >= 2 THEN 1 ELSE 0 END AS CanFinalReviewerEdit
FROM dbo.MT_ReviewDecisions
WHERE QuestionId = @qid AND ReviewStage = 3 AND Decision IN (2, 3);
```

### 6.3 `MT_RevisionReplies` — 命題教師的修題回覆

> **改動**：補上 `Round` 欄位區分本輪與歷史。**「本輪」由 `MAX(Round)` 即時推導，不另存 `IsCurrentRound` flag**，避免進入新一輪時必須清除舊筆 flag 的同步點與飄移風險。

```sql
CREATE TABLE dbo.MT_RevisionReplies (
    Id              INT IDENTITY(1,1) PRIMARY KEY,
    QuestionId      INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Questions(Id),
    UserId          INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Users(Id),
    Stage           TINYINT NOT NULL,    -- 對應 PhaseCode：4=互修, 6=專修, 8=總修
    Round           TINYINT NOT NULL DEFAULT 1,  -- 同一階段第幾次修題
    Content         NVARCHAR(MAX) NOT NULL,
    CreatedAt       DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);

CREATE INDEX IX_MT_RevisionReplies_QuestionId_Stage_Round ON dbo.MT_RevisionReplies(QuestionId, Stage, Round DESC);
```

**「本人是否已送出本輪修題」**（取 Round 最大值即可）：

```sql
SELECT TOP 1 1
FROM dbo.MT_RevisionReplies
WHERE QuestionId = @qid AND UserId = @me
ORDER BY Round DESC;
-- 配合 IX_MT_RevisionReplies_QuestionId_Stage_Round 為單筆 lookup
```

### 6.4 `MT_SimilarityChecks` — 試題相似度查重（不變）

```sql
CREATE TABLE dbo.MT_SimilarityChecks (
    Id                  INT IDENTITY(1,1) PRIMARY KEY,
    SourceQuestionId    INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Questions(Id),
    ComparedQuestionId  INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Questions(Id),
    SimilarityScore     DECIMAL(5,2) NOT NULL,
    Determination       TINYINT NOT NULL,        -- 1=安全, 2=相似度高, 3=確認重複
    CheckedBy           INT FOREIGN KEY REFERENCES dbo.MT_Users(Id),
    CheckedAt           DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);
```

---

## 第七章 — 公告（簡化 Status + 多梯次綁定）

### 7.1 `MT_Announcements` — **DisplayStatus 改為 View 計算 + 拆出多梯次關聯**

> **改動**：
> 1. 拿掉舊 `Status TINYINT (0=草稿, 1=發佈)` + 前端推導四種 DisplayStatus 的雙軌設計，改為：
>    - DB 只存 `IsDraft BIT`（0=已送出, 1=草稿）
>    - DisplayStatus 由視圖 `V_Announcements` 即時計算（用 `SYSDATETIME()` 比對上下架時間）
>    - 前後端都直接讀 `DisplayStatus`，避免不一致
> 2. **拿掉舊 `ProjectId` 單一 FK 欄位**，綁定梯次改用 `MT_AnnouncementTargets` 多對多關聯表（支援同一公告掛多個梯次）。
>    - **無關聯 row = 全站廣播**
>    - **有關聯 row = 只給這些梯次看**
>    - UI 上「全站廣播」與「特定梯次（多選）」二擇一（勾全站時其他鎖死）

```sql
CREATE TABLE dbo.MT_Announcements (
    Id              INT IDENTITY(1,1) PRIMARY KEY,
    Category        TINYINT NOT NULL,
    -- 1=系統公告, 2=命題公告, 3=審題公告, 4=其它

    IsDraft         BIT NOT NULL DEFAULT 1,        -- 草稿與否（取代舊 Status）
    -- ★ 移除舊版 ProjectId 欄位（改由 MT_AnnouncementTargets 管理）

    PublishDate     DATETIME2 NOT NULL,            -- 上架時間
    UnpublishDate   DATETIME2,                     -- 下架時間（NULL=不自動下架）
    IsPinned        BIT NOT NULL DEFAULT 0,

    Title           NVARCHAR(200) NOT NULL,
    Content         NVARCHAR(MAX) NOT NULL,        -- Quill HTML
    AuthorId        INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Users(Id),
    IsDeleted       BIT NOT NULL DEFAULT 0,
    DeletedAt       DATETIME2,
    CreatedAt       DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    UpdatedAt       DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);

CREATE INDEX IX_MT_Announcements_IsDraft_PublishDate ON dbo.MT_Announcements(IsDraft, PublishDate DESC);
```

### 7.2 `MT_AnnouncementTargets` — **公告綁定梯次（多對多）**

> **新增表**：取代舊版 `MT_Announcements.ProjectId` 單欄 FK。
> - 一筆公告可綁定多個梯次
> - 若該 `AnnouncementId` 在此表沒有任何 row → 視為「全站廣播」
> - UI 邏輯：勾「全站廣播」時不寫 row；勾特定梯次時逐一寫入

```sql
CREATE TABLE dbo.MT_AnnouncementTargets (
    AnnouncementId  INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Announcements(Id) ON DELETE CASCADE,
    ProjectId       INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Projects(Id),
    CONSTRAINT PK_MT_AnnouncementTargets PRIMARY KEY (AnnouncementId, ProjectId)
);

-- 反向查詢：「給定一個梯次，找出可見公告」會用到 ProjectId
CREATE INDEX IX_MT_AnnouncementTargets_ProjectId ON dbo.MT_AnnouncementTargets(ProjectId);
```

### 7.3 視圖 `V_Announcements` — **DisplayStatus + 廣播旗標 + 梯次名稱**

```sql
-- 視圖：產生 DisplayStatus、SortKey、IsBroadcast、TargetProjectNames（前後端一律從這個視圖讀）
CREATE OR ALTER VIEW dbo.V_Announcements
AS
SELECT
    a.Id, a.Category, a.IsDraft, a.PublishDate, a.UnpublishDate, a.IsPinned,
    a.Title, a.Content, a.AuthorId, a.IsDeleted, a.DeletedAt, a.CreatedAt, a.UpdatedAt,

    -- 顯示狀態（依當前時間動態判定）
    CASE
        WHEN a.IsDraft = 1                                                          THEN N'草稿'
        WHEN a.PublishDate > SYSDATETIME()                                          THEN N'未發佈'
        WHEN a.UnpublishDate IS NOT NULL AND a.UnpublishDate <= SYSDATETIME()       THEN N'已下架'
        ELSE                                                                              N'已發佈'
    END AS DisplayStatus,

    -- 排序 key：置頂 → 已發佈 → 草稿 → 未發佈 → 已下架 → 分類
    (CASE WHEN a.IsPinned = 1 THEN 0 ELSE 1 END) * 1000000 +
    CASE
        WHEN a.IsDraft = 1                                                          THEN 200
        WHEN a.PublishDate > SYSDATETIME()                                          THEN 100
        WHEN a.UnpublishDate IS NOT NULL AND a.UnpublishDate <= SYSDATETIME()       THEN 300
        ELSE                                                                              0
    END * 10 + a.Category AS SortKey,

    -- ★ 是否全站廣播（=綁定梯次表沒有任何 row）
    CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.MT_AnnouncementTargets t WHERE t.AnnouncementId = a.Id)
         THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS IsBroadcast,

    -- ★ 顯示用：綁定梯次名稱（多筆以「、」串接），全站廣播為 NULL
    (SELECT STRING_AGG(p.Name, N'、')
     FROM dbo.MT_AnnouncementTargets t
     JOIN dbo.MT_Projects p ON p.Id = t.ProjectId
     WHERE t.AnnouncementId = a.Id) AS TargetProjectNames
FROM dbo.MT_Announcements a
WHERE a.IsDeleted = 0;
GO
```

### 7.4 常用查詢樣板

```sql
-- ① 後台列表（管理員看全部，不分梯次）
SELECT * FROM dbo.V_Announcements
ORDER BY SortKey, PublishDate DESC;

-- ② 首頁今日提醒（對特定使用者「在某個梯次的可見公告」）
DECLARE @ProjectId INT = 5;
SELECT v.*
FROM dbo.V_Announcements v
WHERE v.DisplayStatus = N'已發佈'
  AND ( v.IsBroadcast = 1
        OR EXISTS (SELECT 1 FROM dbo.MT_AnnouncementTargets t
                   WHERE t.AnnouncementId = v.Id AND t.ProjectId = @ProjectId) )
ORDER BY v.SortKey, v.PublishDate DESC;

-- ③ 編輯載入：取得某公告綁定的梯次清單
SELECT t.ProjectId, p.Name
FROM dbo.MT_AnnouncementTargets t
JOIN dbo.MT_Projects p ON p.Id = t.ProjectId
WHERE t.AnnouncementId = @AnnouncementId;

-- ④ 儲存：先清空再寫入（在交易中執行）
DELETE FROM dbo.MT_AnnouncementTargets WHERE AnnouncementId = @AnnouncementId;
-- 若使用者勾「全站廣播」→ 不再寫入任何 row
-- 若勾特定梯次 → 逐一寫入
INSERT INTO dbo.MT_AnnouncementTargets (AnnouncementId, ProjectId)
SELECT @AnnouncementId, value FROM OPENJSON(@ProjectIdsJson);
```

> **效益**：
> - 列表頁 `SELECT * FROM V_Announcements ORDER BY SortKey, PublishDate DESC` 直接取得 IsBroadcast 與 TargetProjectNames，前端不用再 join
> - 統計卡片 `GROUP BY DisplayStatus` 一次到位
> - 「給定梯次找可見公告」用 `IsBroadcast=1 OR EXISTS(...)` 一條 WHERE 解決
> - 「自動下架」不再需要排程程式定期跑，只要時間到了 SQL 就自動回 `已下架`

---

## 第八章 — 通知、登入記錄、稽核

### 8.1 `MT_LoginLogs` — 登入登出事件

> **本次改動**：
> - `Id` 由 `INT` 改為 `BIGINT`（log 類資料表會持續累積，BIGINT 較安全）
> - `EventType` 新增 `3 = PasswordReset` 類型（舊版只有 1=Login, 2=Logout）

```sql
CREATE TABLE dbo.MT_LoginLogs (
    Id           BIGINT IDENTITY(1,1) PRIMARY KEY,    -- ★ 由 INT 改為 BIGINT
    UserId       INT FOREIGN KEY REFERENCES dbo.MT_Users(Id),
    Username     NVARCHAR(100) NOT NULL,
    ProjectId    INT FOREIGN KEY REFERENCES dbo.MT_Projects(Id),
    EventType    TINYINT NOT NULL DEFAULT 1,   -- ★ 新增 3=PasswordReset；1=Login, 2=Logout
    IsSuccess    BIT NOT NULL,
    IpAddress    NVARCHAR(50),
    UserAgent    NVARCHAR(500),
    FailReason   NVARCHAR(200),
    CreatedAt    DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);

CREATE INDEX IX_MT_LoginLogs_UserId_CreatedAt ON dbo.MT_LoginLogs(UserId, CreatedAt DESC);
```

### 8.2 `MT_AuditLogs` — 增刪改稽核

> **本次改動**：
> - `Id` 由 `INT` 改為 `BIGINT`（log 類資料表持續累積）
> - `Action` 新增 `3 = Restore`（軟刪除復原；舊版只有 0=Create, 1=Update, 2=Delete）
> - `TargetType` **維持 `TINYINT`**（高頻寫入表，整數比較、索引大小都優於字串）。
>   字串對照名稱（`Users`/`Roles`/⋯/`Reviews`）由 C# `AuditTargetType` enum（`Models/AuditLogEnums.cs`）與 `Dashboard.razor` 的 `TargetTypeLabel(byte)` switch 管理，符合 CLAUDE.md「不常改動的文字以 Model 管理，不另建資料表」原則。

```sql
CREATE TABLE dbo.MT_AuditLogs (
    Id           BIGINT IDENTITY(1,1) PRIMARY KEY,    -- ★ 由 INT 改為 BIGINT
    UserId       INT FOREIGN KEY REFERENCES dbo.MT_Users(Id),
    ProjectId    INT FOREIGN KEY REFERENCES dbo.MT_Projects(Id),
    Action       TINYINT NOT NULL,            -- ★ 新增 3=Restore；0=Create, 1=Update, 2=Delete
    TargetType   TINYINT NOT NULL,            -- 0=Users, 1=Roles, 2=Projects, 3=Questions, 4=Announcements, 5=Teachers, 6=Reviews（對應 AuditTargetType enum）
    TargetId     INT,
    OldValue     NVARCHAR(MAX),               -- JSON
    NewValue     NVARCHAR(MAX),               -- JSON
    IpAddress    NVARCHAR(50),
    CreatedAt    DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);

CREATE INDEX IX_MT_AuditLogs_ProjectId_CreatedAt ON dbo.MT_AuditLogs(ProjectId, CreatedAt DESC);
CREATE INDEX IX_MT_AuditLogs_TargetType_TargetId ON dbo.MT_AuditLogs(TargetType, TargetId);
```

### 8.3 `MT_Notifications` — **站內通知（新增，預留鈴鐺功能）**

> 為了讓導覽列鈴鐺與留言通知功能落地，新增此表。

```sql
CREATE TABLE dbo.MT_Notifications (
    Id           BIGINT IDENTITY(1,1) PRIMARY KEY,
    UserId       INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Users(Id),
    ProjectId    INT FOREIGN KEY REFERENCES dbo.MT_Projects(Id),
    Category     TINYINT NOT NULL,
    -- 1=PhaseAlert（階段倒數提醒）
    -- 2=ReviewAssigned（被指派審題）
    -- 3=RevisionReturned（審題退回給命題老師）
    -- 4=Announcement（系統公告）
    -- 5=Message（留言通知，預留）
    Title        NVARCHAR(200) NOT NULL,
    Body         NVARCHAR(500),
    Url          NVARCHAR(200),               -- 點擊跳轉的目標
    IsRead       BIT NOT NULL DEFAULT 0,
    ReadAt       DATETIME2,
    CreatedAt    DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);

CREATE INDEX IX_MT_Notifications_User_Read ON dbo.MT_Notifications(UserId, IsRead, CreatedAt DESC);
```

### 8.4 `MT_UserGuideFiles` — 操作手冊 PDF

> **本次改動**：新增 `CreatedAt` 欄位，方便依上傳時間排序（舊版沒有任何時間欄位，無從得知檔案上架先後）

```sql
CREATE TABLE dbo.MT_UserGuideFiles (
    Id           INT IDENTITY(1,1) PRIMARY KEY,
    FileName     NVARCHAR(200) NOT NULL,
    FilePath     NVARCHAR(500) NOT NULL,
    FileSize     BIGINT NOT NULL,
    UploadedBy   INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Users(Id),
    IsActive     BIT NOT NULL DEFAULT 1,
    CreatedAt    DATETIME2 NOT NULL DEFAULT SYSDATETIME()    -- ★ 新增
);
```

---

## 第九章 — 階段切換流程（重點工作流範例）

> 這節示範新設計如何讓「階段切換」變流暢。每一個動作只動一張表 + 寫一筆事件。

### 9.1 命題教師按下「命題送審」

```sql
-- 一個 UPDATE 解決，不必碰 PhaseTransitionCoordinator
UPDATE dbo.MT_Questions
SET DraftStage    = 2,                     -- 命題送審
    SubmittedAt   = SYSDATETIME(),
    DisplayBadgeText = N'已送審',
    DisplayBadgeKind = 1,                   -- info 藍
    DisplayUpdatedAt = SYSDATETIME(),
    UpdatedAt     = SYSDATETIME()
WHERE Id = @QuestionId AND CreatorId = @MeId AND Lifecycle = 0;

-- 軌跡：命題階段完成
INSERT INTO dbo.MT_QuestionPhaseLog (QuestionId, PhaseCode, EventType, TriggeredBy)
VALUES (@QuestionId, 2, 2, @MeId);  -- PhaseCode=2 命題, EventType=2 Completed
```

### 9.2 系統推進到「交互審題」階段（梯次階段切換）

```sql
-- 把所有已送審題目鎖入互審
UPDATE dbo.MT_Questions
SET Lifecycle           = 1,                -- InReview
    CurrentReviewStage  = 1,                -- Peer
    DisplayBadgeText    = N'待審',
    DisplayBadgeKind    = 3,                -- warning 橘
    DisplayUpdatedAt    = SYSDATETIME(),
    UpdatedAt           = SYSDATETIME()
WHERE ProjectId = @ProjectId AND Lifecycle = 0 AND DraftStage = 2;

-- 軌跡：互審階段開始
INSERT INTO dbo.MT_QuestionPhaseLog (QuestionId, PhaseCode, EventType)
SELECT Id, 3, 1 FROM dbo.MT_Questions
WHERE ProjectId = @ProjectId AND Lifecycle = 1 AND CurrentReviewStage = 1;

-- 草稿落隊：未送審的草稿改為「未完成命題」紅標
UPDATE dbo.MT_Questions
SET DisplayBadgeText = N'未完成命題',
    DisplayBadgeKind = 4,                   -- danger 紅
    DisplayUpdatedAt = SYSDATETIME()
WHERE ProjectId = @ProjectId AND Lifecycle = 0 AND DraftStage IN (0, 1);
```

### 9.3 互審階段結束 → 進入互修

```sql
-- 一律轉為修題狀態
UPDATE dbo.MT_Questions
SET Lifecycle        = 2,                   -- InRevision
    DisplayBadgeText = N'修題中',
    DisplayBadgeKind = 3,                   -- warning 橘
    DisplayUpdatedAt = SYSDATETIME(),
    UpdatedAt        = SYSDATETIME()
WHERE ProjectId = @ProjectId AND Lifecycle = 1 AND CurrentReviewStage = 1;

-- 軌跡：互審完成 + 互修開始
INSERT INTO dbo.MT_QuestionPhaseLog (QuestionId, PhaseCode, EventType)
SELECT Id, 3, 2 FROM dbo.MT_Questions WHERE ProjectId = @ProjectId AND CurrentReviewStage = 1;

INSERT INTO dbo.MT_QuestionPhaseLog (QuestionId, PhaseCode, EventType)
SELECT Id, 4, 1 FROM dbo.MT_Questions WHERE ProjectId = @ProjectId AND CurrentReviewStage = 1;
```

### 9.4 命題總覽列表查詢（前端只需 SELECT 一次）

```sql
-- 列表頁所需的所有資訊一次撈出，前端不必再寫五條規則
SELECT
    q.Id, q.QuestionCode, q.UpdatedAt,
    q.QuestionTypeId, q.Level, q.Difficulty,
    q.CreatorId,
    q.Lifecycle, q.CurrentReviewStage, q.Outcome,
    -- 「歷史題目」由 q.Outcome != 0 推導，不另存欄位
    q.DisplayBadgeText, q.DisplayBadgeKind,
    -- 七階段燈號從 PhaseLog 一次帶回
    (
        SELECT PhaseCode,
               MIN(CASE WHEN EventType = 1 THEN OccurredAt END) AS Entered,
               MAX(CASE WHEN EventType = 2 THEN OccurredAt END) AS Completed
        FROM dbo.MT_QuestionPhaseLog
        WHERE QuestionId = q.Id
        GROUP BY PhaseCode
        FOR JSON PATH
    ) AS PhaseTrail
FROM dbo.MT_Questions q
WHERE q.ProjectId = @ProjectId AND q.IsDeleted = 0
ORDER BY q.Lifecycle, q.UpdatedAt DESC;
```

---

## 第十章 — 索引總覽

```sql
-- 查詢熱區索引
CREATE INDEX IX_MT_Questions_Project_Lifecycle_Outcome
    ON dbo.MT_Questions(ProjectId, Lifecycle, Outcome);

CREATE INDEX IX_MT_Questions_Creator_Lifecycle
    ON dbo.MT_Questions(CreatorId, Lifecycle);

CREATE INDEX IX_MT_Questions_DisplayBadge
    ON dbo.MT_Questions(ProjectId, DisplayBadgeKind, DisplayBadgeText);

CREATE INDEX IX_MT_Questions_Submitted
    ON dbo.MT_Questions(ProjectId, SubmittedAt DESC) WHERE SubmittedAt IS NOT NULL;
```

---

## 第十一章 — 與舊版的相容性

| 舊表 / 欄位 | 新表 / 欄位 | 遷移策略 |
|---|---|---|
| `MT_Questions.Status` | `Lifecycle / DraftStage / CurrentReviewStage / Outcome` | 用第 5.2 節對照表批次 UPDATE |
| `MT_ReviewReturnCounts.ReturnCount` | 由 `MT_ReviewDecisions` 即時 COUNT(*) | 廢棄該表 |
| `MT_ReviewReturnCounts.CanEditByReviewer` | 即時 SQL 計算（COUNT >= 2） | 廢棄 |
| `MT_ReviewAssignments.ReviewStatus` | 由 `RespondedAt IS NULL` 推導 | 廢棄該欄位 |
| `MT_ReviewAssignments.Decision/Comment` | 由 `MT_ReviewDecisions` `OUTER APPLY TOP 1 ORDER BY DecidedAt DESC` 取得 | 廢棄該欄位（避免兩處同步飄移） |
| `MT_RevisionReplies.IsCurrentRound` | 由 `Round = MAX(Round) GROUP BY QuestionId, UserId` 推導 | 不新增此欄位 |
| `MT_Questions.IsHistorical`（規劃中曾打算新增） | 由 `Outcome != 0` 或 `Project.ClosedAt IS NOT NULL` 推導 | 不新增此欄位 |
| `MT_RolePermissions.Permissions (TINYINT 兩級)` | 廢棄；只留 `IsEnabled BIT` | 全部視為 IsEnabled=1 |
| `MT_Announcements.Status (0/1)` | `IsDraft BIT` + View `V_Announcements.DisplayStatus` | Status=0→IsDraft=1，Status=1→IsDraft=0 |
| `MT_Announcements.ProjectId` | 移到 `MT_AnnouncementTargets(AnnouncementId, ProjectId)` | 舊 ProjectId IS NULL → 不寫入任何 row（廣播）；舊 ProjectId 有值 → INSERT 一筆 row |

---

## 第十二章 — 設計效益總結

| 痛點 | 舊版做法 | 新版做法 | 效益 |
|---|---|---|---|
| 七階段燈號 | 從 `Status` byte 推算 | 從 `MT_QuestionPhaseLog` 直接取 | 軌跡與當前狀態解耦，互不干擾 |
| 當前狀態 Badge | 前端 R1/A/B/R2/C 五條規則 | DB `DisplayBadgeText/Kind` Snapshot | 列表頁讀一次欄位即顯示 |
| 鎖定判斷 | `Status IN (3,5,7)` | `Lifecycle = 1` | 條件單純，索引命中率高 |
| 採用判斷 | `Status IN (9, 12)` | `Outcome = 1` | 不需記住 9 與 12 都算採用 |
| 退回次數 | 另開 `MT_ReviewReturnCounts` 維護 | `COUNT(*) FROM MT_ReviewDecisions` | 沒有同步問題 |
| 第 3 次解鎖總召自改 | `CanEditByReviewer` flag | 同上即時計算 | 無資料漂移風險 |
| 公告 DisplayStatus | 前端推導 | SQL View | 前後端排序一致 |
| 命題教師「本人是否送出本輪修題」 | 多 join 算 | `WHERE UserId = @me ORDER BY Round DESC LIMIT 1` | 配 `IX_RevisionReplies_QuestionId_Stage_Round` 單筆 lookup |
| 審題最新決策 | `MT_ReviewAssignments.LatestDecision/Comment` 快取 | `OUTER APPLY TOP 1 FROM MT_ReviewDecisions` | 不需同步快取，無飄移風險 |
| 歷史題目判斷 | `IsHistorical` snapshot 欄位 | `Outcome != 0` 單欄條件 | 不需批次更新，配 filtered index 命中率高 |

---

## 附錄 A — 各頁面對應的主要查詢

| 頁面 | 主要查詢來源 |
|---|---|
| 命題儀表板 | `MT_Questions` (Lifecycle/Outcome 統計) + `MT_AuditLogs` |
| 命題專案管理 | `MT_Projects` + `MT_ProjectPhases` + `MT_ProjectMembers` + `MT_MemberQuotas` |
| 命題總覽 | `MT_Questions` + `MT_QuestionPhaseLog` (七階段燈號) + `MT_ReviewDecisions` (歷程) |
| 命題任務 - 命題作業區 | `MT_Questions` (Lifecycle=0) + `MT_MemberQuotas` + `MT_QuestionImages`（編輯題目時附圖） |
| 命題任務 - 審修作業區 | `MT_Questions` (Lifecycle=1 鎖定 / 2 修題) + `MT_RevisionReplies` (本輪) + `MT_QuestionImages` |
| 命題任務 - 審核結果歷史 | `MT_Questions` (Lifecycle=3) |
| 審題任務 - 審題作業區 | `MT_ReviewAssignments` + `MT_ReviewDecisions`（歷程） |
| 審題任務 - 審核結果歷史 | `MT_Questions` (Lifecycle=3) |
| 教師管理 - 命題歷程 | `MT_Questions` GROUP BY CreatorId |
| 教師管理 - 審題歷程 | `MT_ReviewDecisions` GROUP BY ReviewerId |
| 教師管理 - 參與專案 | `MT_ProjectMembers` + `MT_ProjectMemberRoles` |
| 角色與權限 | `MT_Roles` + `MT_RolePermissions` + `MT_Modules` |
| 系統公告 | `V_Announcements` |

---

## 附錄 B — 建表順序（部署時依此執行）

1. 字典表：`MT_QuestionTypes` → `MT_Modules` → `MT_Roles` → `MT_RolePermissions`
2. 使用者：`MT_Users` → `MT_Teachers` → `MT_PasswordResetTokens`
3. 專案：`MT_Projects` → `MT_ProjectPhases` → `MT_ProjectTargets` → `MT_ProjectMembers` → `MT_ProjectMemberRoles` → `MT_MemberQuotas`
4. 題目：`MT_Questions` → `MT_QuestionPhaseLog` → `MT_QuestionCodeSequence` → `MT_SubQuestions` → `MT_QuestionImages`
5. 審題：`MT_ReviewAssignments` → `MT_ReviewDecisions` → `MT_RevisionReplies` → `MT_SimilarityChecks`
6. 公告：`MT_Announcements` → `MT_AnnouncementTargets` → 視圖 `V_Announcements`
7. 紀錄：`MT_LoginLogs` → `MT_AuditLogs` → `MT_Notifications` → `MT_UserGuideFiles`
8. 索引（第十章）

---

> **設計理念回顧**：把「現在是什麼」（Snapshot 欄位）與「走過了哪些」（Phase Log 事件流）徹底分開，
> 階段切換時兩邊各自獨立寫入；前端只負責讀，不再做推導。
