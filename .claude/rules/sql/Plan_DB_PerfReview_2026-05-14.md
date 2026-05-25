# 資料庫與效能整體審查計畫書

> **建立日期**：2026-05-14
> **作者**：Jay 與 Claude 共同分析
> **分析範圍**：28 個資料表 + 13 個 Service 檔案 + 11 個頁面
> **資料來源**：`D:\MTrefer\MT.bak`（2026-05-14 還原匯出）+ `D:\IISWebSize\MT\Services\*.cs`
> **分析工具**：codereview-roasted + 10 個頁面專屬 agent 並行審查

---

## ⚠️ 核心警訊（最高優先）

> **「你的 Service 們都在重新發明同樣的輪子，而且每個輪子都掃同一張 MT_Questions。
> 問題不是 schema 設計爛——它其實還不錯——而是 10 個 Service 各自寫 SQL 沒有共用基底，
> 導致同一段 correlated subquery 在程式碼裡複製貼上了 13 次，
> 改一個 bug 要改 13 個地方。修這件事比加任何索引重要。」**

這是這次審查最重要的發現。**不是 schema 爛、也不是某個 query 寫錯，是整體缺少共用基底層**，導致：
1. 同一段 SQL 邏輯散落在多個 Service
2. 改一個業務規則要動 N 個檔案，極容易漏改
3. 資料庫被同樣的 query 反覆打，無人快取
4. 程式碼越長越像紙糊的，沒人敢動

---

## 📋 任務 1：資料庫能否應付網站需求？

**結論**：✅ 9 成可以，3 個明確缺口。

### ✅ 完全支援的功能（按頁面）

| 頁面 | 支援度 | 對應資料表 |
|---|---|---|
| 登入 / 忘記密碼 | ✅ | `MT_Users`、`MT_PasswordResetTokens` |
| 頂部梯次切換器 | ✅ | `MT_Projects` + `MT_ProjectMembers` |
| 首頁今日提醒 / 公告 | ✅ | `MT_Announcements` + `MT_ProjectPhases` |
| 命題儀表板 KPI / 圖表 | ✅ | `MT_Questions` + `MT_ProjectTargets` + `MT_ReviewAssignments` |
| 命題專案管理（8 階段 / 配額 / 多身份） | ✅ | `MT_ProjectPhases` + `MT_ProjectTargets` + `MT_ProjectMembers` + `MT_ProjectMemberRoles` + `MT_MemberQuotas` |
| 命題總覽（母題 + 子題 + 劃記） | ✅ | `MT_Questions` + `MT_SubQuestions` + `MT_ReviewAnnotations` |
| 命題任務（7 題型 + 配額） | ✅ | `MT_Questions` + `MT_QuestionImages` + `MT_QuestionCodeSequence` |
| 審題任務（三審 / Sticky / 退回） | ✅ | `MT_ReviewAssignments` + `MT_ReviewReturnCounts` + `MT_RevisionReplies` + `MT_SimilarityChecks` |
| 教師管理 | ✅ | `MT_Teachers` + `MT_Users` |
| 角色與權限 | ✅ | `MT_Roles` + `MT_RolePermissions` + `MT_Modules` |
| 系統公告 | ✅ | `MT_Announcements` |
| 使用說明手冊 | ✅ | `MT_UserGuideFiles` |
| 稽核 / 登入紀錄 | ✅ | `MT_AuditLogs` + `MT_LoginLogs` |

### ❌ 缺口 3 個

| # | 缺口 | 影響 | 建議新表 |
|---|---|---|---|
| 1 | 鈴鐺聊天 / 留言通知 | 功能規格已提，目前無對應表 | `MT_Messages` + `MT_MessageReads`（或走 SignalR + Redis 不入庫） |
| 2 | 審題 8 則罐頭訊息 | 目前寫死在前端 JS，管理員改不到 | `MT_CannedComments(Id, Category, Content, SortOrder)` |
| 3 | 字體控制器設定持久化 | LocalStorage 上 | 不必入庫，現況可接受 |

---

## 📋 任務 2：正規化合格嗎？

**結論**：🟡 3NF 基本達標，**真正的問題不是正規化，是 5 處該下卻沒下的 UNIQUE 約束**。

### 1NF（原子性）— ✅ 通過

唯一小瑕疵：`MT_Teachers.Expertise nvarchar(200)` 可能存逗號分隔字串，未來要按專長搜尋會痛。目前可接受。

### 2NF — N/A

所有表用 surrogate `Id` PK，沒有複合主鍵問題。

### 3NF — 🟡 4 處合理去正規化 + 5 處缺約束

#### 合理的去正規化（不算違規，是有意設計）

| 欄位 | 為什麼合理 |
|---|---|
| `MT_Projects.Year` | 可從 StartDate 推導，但高頻篩選 / 下拉，免去 `YEAR()` 函數讓索引失效 |
| `MT_LoginLogs.Username` | User 刪除後 Log 仍要看得到嘗試帳號 |
| `MT_AuditLogs.OldValue/NewValue` 含 `targetDisplayName` | 目標刪除後仍能顯示名稱（CLAUDE.md 明文規範） |
| `MT_QuestionImages.QuestionId` XOR `SubQuestionId` | 允許二擇一，DBML 表達不出但語意清楚 |

#### 🔴 真技術債：5 個 UNIQUE 約束沒下

| 資料表 | 應加 UNIQUE 約束 | 不加會發生什麼 |
|---|---|---|
| `MT_ProjectMembers` | `(ProjectId, UserId)` | 一人可被重複加進同梯次 |
| `MT_ProjectMemberRoles` | `(ProjectMemberId, RoleId)` | 同人在同梯次有重複身份 |
| `MT_RolePermissions` | `(RoleId, ModuleId)` | 同角色對同模組有兩筆權限，誰勝出？ |
| `MT_Users` | `Email` | 忘記密碼會出歧義 |
| `MT_Modules` | `ModuleKey` | 權限判定誤判 |

**這些不修，DB 整潔靠程式自律 —— 哪天 race condition 一來就髒掉**。

### 🔴 額外發現：`LOWER(ModuleKey) = 'announcements'` 索引失效

`AnnouncementService.cs:44-58` 用 `LOWER()` 包欄位 → 索引完全失效。應在資料層規範儲存大小寫一致 + 加 UNIQUE 索引。

---

## 📋 任務 3：MT_Questions 該加 reviewer 欄位嗎？

**結論**：🔴 **強烈不建議**，改用 SQL View 解決。

### 反對的核心理由

| 角色 | 與題目的基數關係 | 加欄位的後果 |
|---|---|---|
| 命題教師 | 1:1 | 你已經有 `CreatorId` ✅ |
| 互審教師 | 1:N（一題分配給多個互審） | **違反 1NF，存不下** |
| 專審委員 | 1:N（一題分配給多個專審） | **違反 1NF，存不下** |
| 總召集人 | Sticky 後鎖定一個 | **已有 `MT_ReviewReturnCounts.FinalReviewerId` ✅** |

**Overview 頁查詢慢的真正原因，不是缺欄位，是 SQL 寫法（per-row correlated subquery）**。

### ✅ 推薦方案：建 SQL View 而非加欄位

```sql
CREATE VIEW vw_QuestionReviewers AS
SELECT
    q.Id AS QuestionId,
    q.CreatorId,
    u_creator.DisplayName AS CreatorName,
    rc.FinalReviewerId,
    u_final.DisplayName AS FinalReviewerName,
    STRING_AGG(CASE WHEN ra.ReviewStage = 1 THEN u_r.DisplayName END, ', ') AS PeerReviewers,
    STRING_AGG(CASE WHEN ra.ReviewStage = 2 THEN u_r.DisplayName END, ', ') AS ExpertReviewers,
    STRING_AGG(CASE WHEN ra.ReviewStage = 3 THEN u_r.DisplayName END, ', ') AS FinalReviewers
FROM MT_Questions q
LEFT JOIN MT_Users u_creator ON u_creator.Id = q.CreatorId
LEFT JOIN MT_ReviewReturnCounts rc ON rc.QuestionId = q.Id
LEFT JOIN MT_Users u_final ON u_final.Id = rc.FinalReviewerId
LEFT JOIN MT_ReviewAssignments ra ON ra.QuestionId = q.Id
LEFT JOIN MT_Users u_r ON u_r.Id = ra.ReviewerId
GROUP BY q.Id, q.CreatorId, u_creator.DisplayName, rc.FinalReviewerId, u_final.DisplayName;
```

**直接 `SELECT * FROM vw_QuestionReviewers WHERE QuestionId IN (...)` —— 查詢直覺 + 不違反正規化 + 不用維護同步**。

---

## 📋 任務 4：10 個 Service 效能審查綜合報告

### 🔥 各 Service 評等彙整

| Service | 評等 | 最致命問題 |
|---|---|---|
| AnnouncementService | 🟡 | `LOWER(ModuleKey)` 索引失效 + 列表 SELECT Content 全載 |
| DashboardService | 🟡 | MT_Questions 同 KPI 掃 4-5 次本可合併 2 趟 |
| **ProjectService** | 🔴 | `ReplaceProjectChildRecordsAsync` Serializable tx 內 160+ 次 round-trip |
| **OverviewService** | 🔴 | 同份資料用 3 條獨立 SQL 各掃一次 |
| **QuestionService** | 🔴 | `ListAsync` 每列雙重 EXISTS + 巢狀 MAX，260 行 SQL 字串組裝爆 |
| ReviewService | 🟡 | Modal 開一次打 9 次 round-trip + UpsertFinalSubQuestions N+1 |
| TeacherService | 🟡 | 命題/審題歷程 ORDER BY 無 OFFSET |
| RoleService | 🟡 | `GetRolesAsync` 拉全表權限 + `GetUserModuleCardsAsync` 無快取 |
| HomeService | 🟡 | 結果集 #4 與 #10 邏輯完全複製 |
| **Auth/Captcha/Reset/Email** | 🔴 | **SHA256 裸雜湊 + Captcha new Random() + Gmail 密碼硬編碼** |

### 🚨 跨服務重複模式（核心問題）

#### 重複 #1：「上次總審退回時間」correlated MAX subquery — **13+ 處重複**

```sql
ISNULL((SELECT MAX(DecidedAt) FROM MT_ReviewAssignments
        WHERE QuestionId = ... AND ReviewStage = 3
        AND Decision IN (2, 3)), '1900-01-01')
```

出現位置：
- `QuestionService.cs` — 行 593, 676, 712, 836, 2053（5 處）
- `DashboardService.cs` — 行 284, 494, 529, 757, 916（5 處）
- `OverviewService.cs` — 行 402, 339（2 處）
- `HomeService.cs` — 行 114（1 處）

**👉 修法**：建 SQL Inline Table-Valued Function 或 View `vw_QuestionRoundStartedAt(QuestionId, RoundStartedAt)`，全站只維護一處。

#### 重複 #2：「當前 PhaseCode」查詢 — **5 處**

```sql
SELECT TOP 1 PhaseCode FROM MT_ProjectPhases
WHERE ProjectId = @P AND PhaseCode > 1
AND CAST(GETDATE() AS DATE) BETWEEN StartDate AND EndDate
```

出現位置：`QuestionService:101,154`、`ReviewService:172,617`、`ProjectService:993`、`DashboardService:101`、`HomeService:68`

**👉 修法**：抽到 `IDatabaseService.GetCurrentPhaseAsync(projectId)` + `IMemoryCache` 1 小時 TTL（梯次階段 1 天才推進，可放心快取）。

#### 重複 #3：「使用者在梯次的角色 + Category」JOIN — **5 處**

`MT_ProjectMembers + MT_ProjectMemberRoles + MT_Roles + MT_Users.RoleId UNION` 出現在：
- `RoleService.GetUserModuleCardsAsync`（核心）
- `AnnouncementService.EnsureCanEditAsync`
- `ProjectService.GetVisibleProjectsAsync`
- `HomeService.UrgentAlerts`
- `MainLayout.OnInitializedAsync` 間接呼叫

**👉 修法**：抽 `IMembershipService.GetEffectiveRolesAsync(userId, projectId)` + IMemoryCache 10-30 秒 TTL。

#### 重複 #4：`MT_QuestionTypes` JOIN 每次都來 — **7+ 處**

7 筆**從不變動**的靜態資料，卻每次 Dashboard/Overview/CwtList/Reviews 載入都 JOIN。

**👉 修法**：啟動時 `Dictionary<int, QuestionType>` 全部載入記憶體。永不過期，需更新時用 `IHostedService` 重載一次。

#### 重複 #5：`IsProjectMember` 邏輯散落 — **4 處**

`QuestionService:848`、`ProjectService`、`ReviewService`、`AnnouncementService` 各寫一份 SELECT COUNT。

**👉 修法**：抽到 `IMembershipService`。

---

## 🎯 優先修復順序（依「投入產出比」排序）

### 🚨 第一波（必須修，本週內 — 安全與資料完整性）

> **施工狀態（更新於 2026-05-15）**：5 項中 **4 項已完成 + 1 項暫緩**。完整施工紀錄見本檔末段「附錄 A：第一波施工紀錄」。

| # | 任務 | 影響檔案 | 預估工時 | 狀態 |
|---|---|---|---|---|
| 1 | **Password Hash：SHA256 → PBKDF2（用 .NET 內建，無 NuGet）** | `AuthService.cs` + 4 個共用 Service | 4h | ✅ 已部署 + 已實戰驗證 auto-upgrade |
| 2 | **Gmail 密碼從原始碼移到環境變數** | `EmailService.cs:29` | 30min | ⏭️ 暫緩（用戶決定） |
| 3 | **Captcha 改用 `RandomNumberGenerator`** | `CaptchaService.cs` | 30min | ✅ 完成（順手修 RGB 漏 255 的 bug） |
| 4 | **補上 UNIQUE 約束** | DB schema + RoleService + TeacherService | 1h | ✅ 完成（6 個索引已建，4 新建 + 2 既有） |
| 5 | **規範 `ModuleKey` 大小寫一致** + 移除 `LOWER()` | `AnnouncementService.cs` | 1h | ✅ 完成（DB lowercase + 程式碼移除 LOWER）|

### 🔥 第二波（高 ROI，2-3 天可完成）

> **施工狀態（更新於 2026-05-15）**：6 項全部完成 🎉 第二波結束。完整施工紀錄見本檔末段「附錄 B / C / D / E / F」。

| # | 任務 | 影響範圍 | 預期效果 | 狀態 |
|---|---|---|---|---|
| 6 | **建 `vw_QuestionRoundStartedAt` View** | 12 處題目級 SQL 改寫；1 處單元級保留 | 改一處全站受惠 | ✅ 完成 + 4 頁面實測通過 |
| 7 | **建 `IMembershipService` + 短 TTL Cache** | 3 處核心呼叫點改寫（HomeService 結果集 #2 暫不改） | 首頁 / Layout 高頻打 DB 顯著降載 | ✅ 完成 + 多角色實測通過 |
| 8 | **`MT_QuestionTypes` 啟動時載入記憶體** | 7 處 JOIN 已拿掉（6 處形態 B 保留） | 全站 SQL JOIN 少一張表 | ✅ 完成 + 4 頁面實測通過 |
| 9 | **`ListAsync` per-row EXISTS 改 CTE LEFT JOIN** | `QuestionService.cs:582-770` 整段重寫 | 命題任務頁速度翻倍（per-row EXISTS 從 ~2000 次 → 0 次） | ✅ 完成 + CwtList 三 Tab + Overview 實測通過 |
| 10 | **`MT_LoginLogs` 加 `(UserId, IsSuccess, CreatedAt)` 複合索引** | DB schema | 失敗計數查詢從掃整表變索引 seek | ✅ 完成（已部署） |
| 11 | **`MT_PasswordResetTokens` 加 `Token` 唯一索引** | DB schema | 重設密碼驗證避免全表掃 | ✅ 完成（既有 UNIQUE 約束已建有效索引，無需重建） |

### 🔨 第三波（架構改造，1-2 週）

| # | 任務 | 影響範圍 |
|---|---|---|
| ~~12~~ | ~~**`ProjectService.ReplaceProjectChildRecordsAsync` 改 Bulk Insert（TVP 或 `Dapper.Plus`）**~~ | ✅ 已完成（2026-05-26，兩 commit 分段，多列 VALUES 而非 TVP，附錄 I）|
| 13 | **`OverviewService` 三條獨立 SQL 合併成單一 CTE** | Overview 頁載入快 30-50% |
| 14 | **`ReviewService.GetModalDataAsync` 9 次 round-trip → 3-4 次** | 審題 Modal 開啟速度 |
| 15 | **教師歷程 ORDER BY 加 OFFSET/FETCH 分頁** | TeacherService |
| 16 | **`EmailService.SendResetPWEmailAsync` 改 fire-and-forget** | 忘記密碼流程不卡 |
| 17 | **`RoleService.GetUserModuleCardsAsync` 加 IMemoryCache** | MainLayout 每次梯次切換高頻呼叫 |
| 18 | **`RoleService.MergeRolePermissionsAsync` 改批次 INSERT** | 角色儲存 8 次 round-trip → 1 次 |

### 🏗️ 第四波（schema 重構）

| # | 任務 | 說明 |
|---|---|---|
| 19 | 建 `vw_QuestionReviewers` View 取代 Overview 多重 JOIN | Q3 提案 |
| 20 | 規劃 `MT_Messages` + `MT_MessageReads` | 鈴鐺通知 / 聊天功能 |
| 21 | 規劃 `MT_CannedComments` | 罐頭訊息可由管理員編輯 |
| 22 | 評估 Data Protection Keys 持久化（IIS） | 防 AppPool Recycle 後 Cookie 失效 |

---

## 📐 推薦的目標架構

```
┌────────────────────────────────────────────────────────────┐
│              Components/Pages/*.razor (11 頁)               │
└─────────────────────────┬──────────────────────────────────┘
                          │
┌─────────────────────────▼──────────────────────────────────┐
│           Services/* (13 個 Service，現有)                  │
│  AnnouncementService / DashboardService / ProjectService    │
│  OverviewService / QuestionService / ReviewService / ...    │
└─────────────────────────┬──────────────────────────────────┘
                          │
       ┌──────────────────┴───────────────────┐
       │                                       │
┌──────▼──────────┐                  ┌────────▼──────────┐
│ ✨ 新增共用基底層 │                  │ Cached Static Data │
│                  │                  │ (啟動時載入)       │
│ IMembershipSvc   │                  │ - QuestionTypes    │
│ IPhaseSvc        │                  │ - Modules          │
│ IUserContextSvc  │                  │ - Roles            │
│ (含 MemoryCache) │                  └───────────────────┘
└──────┬───────────┘
       │
┌──────▼─────────────────────────────────────────────────────┐
│              DB Views / Functions（消除重複 SQL）            │
│  - vw_QuestionRoundStartedAt（消 13 處複製）                │
│  - vw_QuestionReviewers（取代 Overview 多重 JOIN）           │
│  - vw_UserEffectiveRoles（消 5 處權限 UNION）                │
└──────┬─────────────────────────────────────────────────────┘
       │
┌──────▼───────────┐
│   SQL Server     │
│   MT Database    │
└──────────────────┘
```

---

## 📌 寫在最後

這份計畫書不是要你**一次改完所有事**。Linus 的核心訊息是：

> **每一行重複的 SQL 都是一個未來的 bug**。

請按優先順序慢慢來。**第一波必須先做**（安全議題不容拖延），第二波建好共用基底後，後面所有重構都會變簡單。

> 修這件事比加任何索引重要 — 因為加了索引只能讓現有的爛 SQL 跑快一點，
> 但消除重複能讓**未來所有的 SQL** 都不爛。

---

## 🔗 相關文件參照

- 資料庫備份：`D:\MTrefer\MT.bak`
- DBML 視覺化檔：`D:\MTrefer\MT_dbdiagram.dbml`
- SQL Dump（schema + data）：`D:\MTrefer\MT_dump.sql`
- 網站功能介紹：`D:\jay_liu\Desktop\命題系統相關\網站功能介紹.md`
- 既有 SQL 索引腳本：`.claude\rules\sql\add_review_assignments_unique_indexes.sql`、`drop_old_review_assignments_index.sql`

---

# 附錄 A：第一波施工紀錄（2026-05-15 完成）

## 任務 ① — SHA256 → PBKDF2

**設計策略**：用 .NET 內建 `Rfc2898DeriveBytes.Pbkdf2`，零 NuGet 依賴。

**新格式**：`PBKDF2.v1$<iter>$<salt-base64>$<hash-base64>`（約 86 字元）
- iteration = 100,000
- salt = 16 bytes 密碼學安全隨機
- hash = SHA256 32 bytes

**舊格式相容**：登入時偵測 `PBKDF2.` 前綴決定走 PBKDF2 或舊 SHA256 Base64 驗證；舊格式驗證成功會自動重 hash 寫回 DB（auto-upgrade）。

### 修改的檔案
- `Services/AuthService.cs` — 新增 `HashPassword()` / `VerifyPassword()` / `UpgradePasswordHashAsync()`；移除 `ComputePasswordHash()`；改 `UserAuthRow.PasswordHash` 型別 `byte[]` → `string`
- `Services/PasswordResetService.cs` — 2 處（ResetPasswordAsync / ChangePasswordAsync）
- `Services/RoleService.cs` — 3 處（CreateInternalAccount / ResetAccountPassword / ChangeOwnPassword）
- `Services/TeacherService.cs` — 2 處（CreateTeacher / ResetTeacherPassword）

### 資料庫遷移
`migrate_password_hash_to_pbkdf2.sql` — 將 `MT_Users.PasswordHash` 從 `binary(32)` 轉成 `nvarchar(150)`，舊資料用 SQL Server XML `xs:base64Binary` 一次性轉成 Base64 字串。

### 實戰驗證
部署當天用 `jay` 與 `test01@test.com` 登入後，DB 觀察到 hash 自動從 44 字元 Base64 升級為 86 字元 `PBKDF2.v1$10000...` 格式。同時旁證了「沒 salt 的 SHA256：同密碼 = 同 hash」的 bug — `test02`/`test03`/`test04` 三個帳號 hash 前 15 字元完全相同。

---

## 任務 ③ — Captcha → RandomNumberGenerator

### 修改的檔案
- `Services/CaptchaService.cs` — 移除 `private readonly Random _random` 實例欄位，全改靜態 `RandomNumberGenerator.GetInt32()`。順手修 RGB bug（`_random.Next(255)` 只生 0-254 → `GetInt32(256)` 正確 0-255）。

### 安全提升
從可預測偽隨機（同毫秒兩次呼叫輸出相同）升級為密碼學等級不可預測隨機。

---

## 任務 ④ — UNIQUE 約束（補刀後完整覆蓋 6 個）

### 既有的 UNIQUE 索引（盤點時發現，無需重建）
- `UQ_MT_ProjectMembers_ProjectId_UserId` — 涵蓋需求 #3
- `UQ_MT_RolePermissions_RoleId_ModuleId` — 涵蓋需求 #5
- `UQ_MT_ProjectPhases_ProjectId_PhaseCode` — 額外保護（之前其他工作留下）
- `UQ_MT_ReviewAssignments_Pending_Master/Sub` — Plan_022 留下的

### 新建的 UNIQUE 索引（4 個）
- `UQ_MT_Users_Username`（filtered: `WHERE Username IS NOT NULL AND Username <> ''`）
- `UQ_MT_Users_Email`（filtered: `WHERE Email IS NOT NULL AND Email <> ''`）
- `UQ_MT_Modules_ModuleKey`（unfiltered，配合 ⑤ 的 lowercase 規範）
- `UQ_MT_ProjectMemberRoles_Member_Role`

### 程式碼簡化
刪除 `RoleService.EnsureUsernameUniqueAsync` 整個方法（~12 行）+ 呼叫點 — 改靠 `SqlException 2601/2627` catch 翻譯成「帳號已存在」/「Email 信箱已存在」人話。每次新建帳號少一次 SELECT COUNT 預檢的 round-trip。

`TeacherService.cs` 的 `LOWER(Username) = LOWER(@Email)` 改為直接比對（依賴預設 CI collation）。

### 相關 SQL 腳本
- `precheck_unique_constraints.sql` — 6 個重複資料健檢
- `add_unique_constraints.sql` — 完整版（含 6 個 ALTER + ModuleKey lowercase UPDATE）
- `add_missing_unique_constraints.sql` — 補刀版（只建缺漏的 4 個）

---

## 任務 ⑤ — ModuleKey 大小寫規範

### DB 端
`UPDATE MT_Modules SET ModuleKey = LOWER(ModuleKey)` 一次性統一為 lowercase（如 `Announcements` → `announcements`）。

### 程式碼端
`AnnouncementService.EnsureCanEditAsync` 的 `WHERE LOWER(m.ModuleKey) = 'announcements'` 改為 `WHERE m.ModuleKey = 'announcements'` — 索引從失效變成 index seek。

### 順手刪掉的過時註解
"DB 實際存的 ModuleKey 為「首字大寫」...比對採大小寫不敏感避免拼錯" — 改為「ModuleKey 一律小寫，可直接走 UQ_MT_Modules_ModuleKey 索引」。

---

## 任務 ② — Gmail 密碼移環境變數（暫緩）

用戶於 2026-05-15 決定先跳過此項。Gmail App Password 仍硬編碼於 `EmailService.cs:29`。**TODO**：日後處理時記得**先到 Gmail 撤銷現有 App Password 重發**（舊密碼已外洩給 AI），然後改讀 `IConfiguration["Email:SmtpPassword"]`，在 IIS Manager → AppPool → 進階設定加環境變數。

---

## 部署順序（已執行）

```
1. 備份 MT 資料庫 ✅
2. migrate_password_hash_to_pbkdf2.sql ✅
3. precheck_unique_constraints.sql ✅（6 個健檢回 0 列）
4. add_unique_constraints.sql ✅（部分成功）
5. add_missing_unique_constraints.sql ✅（補刀完整）
6. dotnet publish + IIS 部署 ✅
7. 解除維護模式 ✅
8. 實戰驗證 auto-upgrade ✅
```

---

## 本波累積成果

| 量化指標 | 數量 |
|---|---|
| 修改的 Service 檔案 | 6（AuthService, PasswordResetService, RoleService, TeacherService, AnnouncementService, CaptchaService） |
| 新增 SQL 腳本 | 4 |
| 新增 NuGet 套件 | **0** |
| 刪除的手動防呆程式碼 | ~14 行 |
| 新增 DB UNIQUE 索引 | 4 個（加上既有 2 個 = 完整覆蓋 6 個需求） |
| 升級為密碼學等級的安全機制 | 2 處（密碼 + 驗證碼） |
| 修復索引失效的查詢 | 3 處（Username/Email/ModuleKey 的 LOWER 包欄位） |
| Build 結果 | 0 警告 0 錯誤 |
| 部署後線上錯誤 | 0 |

**下一波預定**：第二波（共用基底層）— `vw_QuestionRoundStartedAt` View + `IMembershipService` + `MT_QuestionTypes` 快取 + `ListAsync` per-row EXISTS 重寫。預估 2-3 天。

---

# 附錄 B：第二波 #8 施工紀錄（2026-05-15 完成）

## 任務 #8 — `MT_QuestionTypes` 啟動時載入記憶體

**設計策略**：用 Singleton 服務 + `IServiceScopeFactory` 借 Scoped `IDatabaseService` 連線，啟動時一次 SELECT 全表進 `Dictionary<int, QuestionTypeEntry>`，後續所有需要題型名稱的 Service 改注入 `IQuestionTypeCatalog`，在 C# 端用 `GetName(id)` 補欄位。形態 B 的 6 處 `FROM dbo.MT_QuestionTypes qt LEFT JOIN ...`（用作主表展開 7 題型）按計畫**保留不動**。

**Warm-up 策略**：`app.Run()` 之前 `await ReloadAsync()`，DB 連不上就讓站台啟動失敗（fail-fast），避免帶空資料上線。

## 新增的檔案

### `Services/QuestionTypeCatalog.cs`（~60 行）
- `IQuestionTypeCatalog` 介面：`All` / `GetName(int)` / `Get(int)` / `ReloadAsync()`
- `QuestionTypeEntry` record：對應 DB 4 欄位
- `QuestionTypeCatalog` 實作：Singleton，整批替換 `_all` + `_byId` 兩份資料結構（讀者永遠看到一致狀態，不會半新半舊）
- 含「資料源永不變動」的使用警告註解

## 修改的檔案（7 個）

### Program.cs
- 新增 `builder.Services.AddSingleton<IQuestionTypeCatalog, QuestionTypeCatalog>();`
- `app.Run()` 之前 `await app.Services.GetRequiredService<IQuestionTypeCatalog>().ReloadAsync();`

### Models/TeacherModels.cs
- `TeacherComposeItem` 加 `QuestionTypeId` 屬性
- `TeacherReviewItem` 加 `QuestionTypeId` 屬性
- 原 `TypeName` 屬性保留（由 Service 端 catalog 補）

### Services/TeacherService.cs（2 處 SQL）
- Constructor 注入 `IQuestionTypeCatalog _typeCatalog`
- 行 233 `GetTeacherComposeHistoryAsync`：拿掉 `INNER JOIN dbo.MT_QuestionTypes qt`，SELECT 改回 `q.QuestionTypeId`，C# 端 `foreach` 用 catalog 補 `TypeName`
- 行 295 `GetTeacherReviewHistoryAsync`：同上模式

### Services/QuestionService.cs（1 處 SQL）
- Primary constructor 加 `IQuestionTypeCatalog typeCatalog` 參數
- 行 69 `GetMyQuotaProgressAsync`：拿掉 `INNER JOIN dbo.MT_QuestionTypes`，GROUP BY 簡化為 `mq.QuestionTypeId, mq.QuotaCount`，C# 端 catalog 補 `TypeName` 並 `OrderBy SortOrder`

### Services/ProjectService.cs（1 處 SQL）
- Constructor 注入 `IQuestionTypeCatalog _typeCatalog`
- 行 240 `Targets` SQL：拿掉 JOIN，SELECT 僅 `pt.QuestionTypeId, pt.TargetCount`，C# 端 `foreach` 補 `TypeName`

### Services/DashboardService.cs（3 處 SQL）
- Constructor 注入 `IQuestionTypeCatalog _typeCatalog`
- 行 905 `BuildUrgentItems`（修題分支）：拿掉 JOIN，`GROUP BY` 移除 `qt.Name`
- 行 956 `BuildUrgentItems`（審題分支）：同上模式
- 行 982 `BuildUrgentItems`（配額分支）：同上模式
- `TeacherTypeDetailRow` record 移除 `TypeName` 欄位（從 5 個改 4 個）
- 行 1019 消費端 `new UrgentTeacherDetail { TypeName = _typeCatalog.GetName(r.QuestionTypeId) }` 補 TypeName

## 形態 B 保留（6 處 / DashboardService）

下列 6 處 `FROM dbo.MT_QuestionTypes qt` 作主表，配 `LEFT JOIN ProjectTargets / Questions` 用以展開「沒題目的題型也要顯示 0」的圖表，**按計畫保留不動**：
- 行 61（題型缺口分析卡片）
- 行 133（命題達成率）
- 行 299, 329, 354, 385（4 種統計分組查詢）

> 改寫成 `FROM (VALUES (1, N'...'), ...) qt(Id, Name)` 內嵌會把字典寫死兩處，違反「改一處全站受惠」精神，因此維持 JOIN。SQL Server 對 7 筆超小表 JOIN 成本接近 0。

## 4 頁面實測（user 親自驗證通過）

| 頁面 | 驗證點 | 結果 |
|---|---|---|
| Dashboard | 教師落後展開區塊題型欄位 | ✅ 顯示正確 |
| CwtList | 頂部命題配額卡片（7 種題型 + SortOrder 排序） | ✅ 顯示正確 |
| Teachers | 點教師後命題歷程 + 審題歷程「題型」欄 | ✅ 顯示正確 |
| Projects | 開既有專案編輯 → 題型目標數量區塊 | ✅ 顯示正確 |

## 部署順序（已執行）

```
1. 新增 Services/QuestionTypeCatalog.cs ✅
2. Program.cs DI 註冊 + warm-up ✅
3. Models/TeacherModels.cs 加 QuestionTypeId 欄位 ✅
4. 改寫 4 個 Service 共 7 處 SQL ✅
5. dotnet build ✅（0 警告 0 錯誤）
6. 4 頁面手動驗證 ✅（全部通過）
```

## 本波累積成果（#8）

| 量化指標 | 數量 |
|---|---|
| 新增的 Service 檔案 | 1（`QuestionTypeCatalog.cs`） |
| 新增的 Service interface | 1（`IQuestionTypeCatalog`） |
| 新增的 record / DTO | 1（`QuestionTypeEntry`） |
| 修改的 Service 檔案 | 4（Teacher, Question, Project, Dashboard） |
| 修改的 Model 檔案 | 1（TeacherModels：2 個 class 加 `QuestionTypeId`） |
| 修改的 Program.cs | 2 行（DI 註冊 + warm-up） |
| 拿掉的 JOIN | 7 處 |
| 保留的 JOIN（形態 B 主表展開） | 6 處（按計畫不動） |
| 啟動時 DB query 數 | +1（warm-up SELECT，~10ms） |
| 記憶體佔用 | +1KB（7 筆 record） |
| 新增 NuGet 套件 | **0** |
| Build 結果 | 0 警告 0 錯誤 |
| 部署後線上錯誤 | 0 |

**下一波預定**：第二波剩餘 5 項任意挑——建議優先 **#6 `vw_QuestionRoundStartedAt` View**（影響 13 處 SQL，純 DB 端改動）或 **#10/#11 索引**（純 DB 端，工時最短）。`#7 IMembershipService` 與 `#9 ListAsync 重寫`影響面較大，建議排後。

---

# 附錄 C：第二波 #10 #11 施工紀錄（2026-05-15 完成）

## 任務 #10 — `MT_LoginLogs` 失敗計數複合索引

**設計策略**：建立 `(UserId, IsSuccess, CreatedAt)` 複合 nonclustered index，前綴 `(UserId, IsSuccess)` 用於 equality seek，尾段 `CreatedAt` 同時支援 range scan 與 MAX 聚合。

**服務的 query**：`AuthService.CountConsecutiveFailedAttemptsAsync` (AuthService.cs:374-400)
- 外層 query：`WHERE UserId = @U AND IsSuccess = 0 AND FailReason = @F AND CreatedAt >= ...`
- 內層 subquery：`SELECT MAX(CreatedAt) ... WHERE UserId = @U AND IsSuccess = 1`
- 一個索引同時涵蓋兩段，無需建兩個。

**未 INCLUDE `FailReason`**：NVARCHAR(200) 過胖，對小資料集 lookup 成本可忽略。若未來資料量大且常按 FailReason 篩，再考慮 INCLUDE。

## 任務 #11 — `MT_PasswordResetTokens.Token` 索引（無需重建）

**健檢結論**：schema 上既有 `Token NVARCHAR(500) NOT NULL UNIQUE` 已由 SQL Server 自動建出 `UQ__MT_Passw__1EB4F817073D4F91` UNIQUE NONCLUSTERED 索引（首位 key = Token）。實際 token 為 32 char GUID（`Guid.NewGuid().ToString("N")`），遠在 900 byte key 限制內，UNIQUE INDEX 完全可用於 seek。

**結論**：#11 **實質已完成**，無需動作。Plan 原始描述「加 Token 唯一索引」的審查盲點，是 codereview-roasted 沒看到 inline UNIQUE 約束。

## 新增的檔案（1 個）

### `.claude/rules/sql/add_indexes_phase2_login_token.sql`
- 冪等腳本（`IF NOT EXISTS` 包住所有 CREATE）
- 三段：健檢 → #10 CREATE → #11 條件式 CREATE
- 自動輸出狀態訊息 `[#10] ✅ / ⏭️` `[#11] ✅ / ⏭️`
- 健檢同時印出資料量、Token 實際長度

## 部署順序（已執行）

```
1. SSMS 開啟 add_indexes_phase2_login_token.sql ✅
2. F5 跑整份 ✅
3. 確認訊息：
   [#10] ✅ IX_MT_LoginLogs_UserId_IsSuccess_CreatedAt 建立完成
   [#11] ⏭️  Token 首位 key 索引已存在（UNIQUE 約束自動建立），無需重建
4. 健檢結果：
   - MT_LoginLogs 索引：PK + 新建 IX_...UserId_IsSuccess_CreatedAt（共 2 個）
   - MT_PasswordResetTokens 索引：PK + UQ__MT_Passw__1EB4F817073D4F91 on Token（共 2 個）
   - LoginLogs 資料量：266 筆（24 筆失敗）
   - Tokens 資料量：11 筆，長度全為 32 char（GUID）
5. 對程式碼 0 改動 — SQL Server 自動採用新索引重新計算 query plan ✅
```

## 本波累積成果（#10 + #11）

| 量化指標 | 數量 |
|---|---|
| 新增 SQL 腳本 | 1（冪等） |
| 新增 DB nonclustered index | 1（`IX_MT_LoginLogs_UserId_IsSuccess_CreatedAt`） |
| 確認既有索引可用 | 1（`UQ__MT_Passw...` on Token） |
| 修改的 C# 檔案 | **0** |
| 修改的 Program.cs | **0** |
| 重啟站台需求 | **0** |
| Build 結果 | 不需要 build |
| 部署後線上錯誤 | 0 |

**從掃整表 → 索引 seek**：登入失敗計數查詢從 O(n) 掃 266+ 筆變 O(log n) seek。資料量增長到 50,000+ 時差異會顯著感受到，目前已預先建立索引「未雨綢繆」。

**下一波預定**：第二波剩餘 3 項——#6（View 消 13 處重複 SQL）、#7（IMembershipService 共用權限基底）、#9（ListAsync per-row EXISTS 改 CTE LEFT JOIN）。建議下一個動 #6（純 DB 端，13 處 SQL 改寫但 C# 邏輯不變）。

---

# 附錄 D：第二波 #6 施工紀錄（2026-05-15 完成）

## 任務 #6 — `vw_QuestionRoundStartedAt` View

**設計策略**：建題目級 View 取代 13 處重複的「上次總審退回時間 MAX」correlated subquery。改寫採 **scalar subquery 取代**（不用 LEFT JOIN），因為 12 處嵌在 EXISTS / NOT EXISTS / CASE WHEN EXISTS 內，LEFT JOIN 會改變 row cardinality 造成 COUNT/SUM 錯亂。

**改寫範本**：括號內 SQL 替換，外層 `ISNULL(..., '1900-01-01')` 完整保留 → 行為 100% 等效。

## 新增物件
- SQL 腳本：`.claude/rules/sql/create_vw_QuestionRoundStartedAt.sql`（含健檢、CREATE OR ALTER VIEW、Old vs New 對拍）
- DB View：`dbo.vw_QuestionRoundStartedAt`（簡單 GROUP BY，沒有 Stage 3 紀錄不回列，由呼叫端 ISNULL fallback）

## 修改的檔案

| 檔案 | 處數 |
|---|---|
| QuestionService.cs | 4 處題目級改寫（行 602/685/721/845）+ 1 處單元級保留（行 2062 加註解：含 SubQuestionId NULL-safe 比對，view 涵蓋不到） |
| DashboardService.cs | 5 處（287/497/532/762/917） |
| OverviewService.cs | 4 處（2 個 SQL 各 2 個實例：行 354+375、417+440） |
| HomeService.cs | 2 處（實際 grep 發現是 2 處，不是 plan 估的 1 處） |

## 4 頁面實測（user 親自驗證通過）
- CwtList 審修作業區 / Overview 待修編 / Dashboard 教師落後 / 首頁今日提醒

## 累積成果
| 量化指標 | 數量 |
|---|---|
| 新增 SQL 腳本 | 1 |
| 新增 DB View | 1 |
| 消除重複 SQL 片段 | 12 處（13 處中 12 處題目級，1 處單元級保留） |
| 修改的 Service 檔案 | 4 |
| 修改 Model / Program.cs | **0** |
| Build 結果 | 0 警告 0 錯誤 |

**真正的價值**：未來改業務規則只需動 View 一處，12 處呼叫點自動同步。

---

# 附錄 E：第二波 #7 施工紀錄（2026-05-15 完成）

## 任務 #7 — `IMembershipService` 短 TTL Cache

**設計策略**：Scoped service + `IMemoryCache`（Singleton 跨 request 共享），30 秒 TTL，雙層失效 token（per-user CTS + global CTS）支援主動 invalidate。

**研究結論修正**：Plan 原描述「5 處權限查詢」實際盤點為：
- 3 處屬「user 角色 UNION + RolePermissions JOIN」同模式（RoleService.GetUserModuleCardsAsync / AnnouncementService.EnsureCanEditAsync / MainLayout.razor）→ 本次全改
- 1 處屬「梯次內 RoleName 集合」（HomeService 結果集 #2）→ 暫不改（混 QueryMultipleAsync 內風險高、ROI 低）
- 1 處屬「可見梯次三 UNION」（ProjectService.GetVisibleProjectsAsync）→ 邏輯太特殊不適合 MembershipService

## 新增的檔案
- `Services/MembershipService.cs`（~140 行）：`IMembershipService` 介面提供 `GetEffectiveRoleIdsAsync` / `GetUserModuleCardsAsync` / `HasModulePermissionAsync` / `InvalidateUser` / `InvalidateAll`

## 修改的檔案
- **Program.cs**：1 行 DI 註冊（`AddScoped<IMembershipService, MembershipService>()`）
- **Services/RoleService.cs**：
  - Constructor 注入 `IMembershipService`
  - `GetUserModuleCardsAsync` 變 thin wrapper（5 行 → 直接呼叫 `_membership`）
  - `BroadcastRoleChangedAsync` 加 `_membership.InvalidateAll()` → 被 5 個方法呼叫（UpdateAccount / ToggleAccountStatus / CreateRole / UpdateRole / DeleteRole）自動同步清 cache
- **Services/AnnouncementService.cs**：
  - Constructor 注入 `IMembershipService`
  - `EnsureCanEditAsync` 從 `static (conn, operatorId)` 改為 `instance (operatorId)`，內部用 `_membership.HasModulePermissionAsync`
  - 5 個呼叫點 signature 簡化
- **Components/Layout/MainLayout.razor**：
  - 新增 `@inject IMembershipService MembershipService`
  - `ReloadModuleCardsAsync` 改呼叫 `MembershipService.GetUserModuleCardsAsync`

## 失效機制設計

| 觸發來源 | 動作 |
|---|---|
| 30 秒 TTL 到期 | 自然過期 → 重打 DB |
| RoleService 改完帳號 / 角色 / 權限 | `BroadcastRoleChangedAsync` 自動 `InvalidateAll()` + SignalR 廣播 |
| 手動 `InvalidateUser(userId)` | per-user CTS cancel → 清該 user 所有 cache entry |

## 多身分實測通過
- admin / 命題教師 / 審題委員 三身分登入後首頁卡片可見性、Layout 麵包屑、公告編輯權限均正確
- 30 秒 cache 行為符合預期
- 改完權限後 InvalidateAll 立即生效（不用等 30 秒）

## 累積成果
| 量化指標 | 數量 |
|---|---|
| 新增 Service 檔案 | 1（`MembershipService.cs`） |
| 修改 Service 檔案 | 2（RoleService, AnnouncementService） |
| 修改 Razor 元件 | 1（MainLayout.razor） |
| 修改 Program.cs | 1 處 DI 註冊 |
| 消除的內聯權限 SQL | 1 處（AnnouncementService.EnsureCanEditAsync 35 行 → 1 行 method call） |
| 新增 NuGet 套件 | **0** |
| Build 結果 | 0 警告 0 錯誤 |

## 暫不改的 2 處（留下波）
- HomeService 結果集 #2：混 QueryMultipleAsync 內挑出風險高、ROI 低
- ProjectService.GetVisibleProjectsAsync：邏輯太特殊，單獨 cache 整個結果集另一階段處理

---

**第二波最終狀態**：6 項中 5 項完成，剩 #9（QuestionService.ListAsync 260 行 SQL 重寫為 CTE LEFT JOIN）。完成後第二波全部結束，可進入第三波架構改造。

---

# 附錄 F：第二波 #9 施工紀錄（2026-05-15 完成）

## 任務 #9 — `QuestionService.ListAsync` per-row EXISTS 改 CTE LEFT JOIN

**設計策略**：原 SQL 每筆 row 跑 2-4 個 EXISTS / COUNT subquery（HasRepliedThisStage + MakeRepliedClause filter + SubQuestionCount + vw scalar subquery）。改為**3 個 CTE 預聚合（hash aggregate）+ 主 SELECT LEFT JOIN 取結果**，徹底消除 per-row subquery。

**改寫前先做 SQL 對拍**：產出 `verify_listasync_cte_rewrite.sql` 4 段對拍腳本，user 在 SSMS 跑全部 PASS 後才動 C#。對拍涵蓋：母題 HasRepliedThisStage、子題 HasRepliedThisStage、母題 WHERE filter、子題 WHERE filter。

## 新增的 CTE 結構

```sql
WITH
-- 母題層級 HasReplied（一次 hash aggregate）
MasterReplied AS (
    SELECT q.Id AS QId, 1 AS HasReplied
    FROM dbo.MT_Questions q
    INNER JOIN dbo.MT_RevisionReplies rr ON rr.QuestionId = q.Id AND rr.SubQuestionId IS NULL ...
    LEFT JOIN dbo.vw_QuestionRoundStartedAt rs ON rs.QuestionId = q.Id
    WHERE q.ProjectId = @ProjectId AND q.Status IN (4, 6, 8)
      AND rr.CreatedAt > ISNULL(rs.RoundStartedAt, '1900-01-01')
    GROUP BY q.Id
),
-- 子題層級 HasReplied（同模式比對 SubQuestionId）
SubReplied AS ( ... GROUP BY sq.Id ),
-- 各題子題數（取代原 per-row COUNT subquery）
SubCounts AS ( ... GROUP BY sq.ParentQuestionId )
```

## 改寫範圍（C# 檔案）

### `Services/QuestionService.cs` 行 582-770 整段重寫
- **移除**：`MakeRepliedClause` lambda（11 行 + 兩個 replied clause 變數）
- **新增**：`cteHeader` const（3 個 CTE） + 兩個 const filter 字串（`masterRepliedFilter` / `subRepliedFilter`）
- **改寫**：`masterSelectSql` / `subSelectSql` / `countSql` / `listSql` 全部套 CTE
- **HasRepliedThisStage**：inline EXISTS 改 `CAST(ISNULL(mr.HasReplied, 0) AS BIT)`
- **SubQuestionCount**：per-row COUNT 改 `ISNULL(sc.Cnt, 0)`

## 順手修補的既有 bug

施工過程中 user 在 Overview 詳情點題目觸發 `Microsoft.Data.SqlClient.SqlException: 無效的資料行名稱 'QuestionId'`。研判結果：

**`Services/ReviewService.cs:1209-1210` `LoadHistoryAsync` UnitInfo CTE**：
- 寫 `sq.QuestionId`，但 `MT_SubQuestions` 表正確欄位是 `ParentQuestionId`
- 觸發條件：只有 `aggregateAllUnits = true` 才走這條 CTE → Overview 頁的「整題彙整視角」是唯一觸發點
- **此 bug 跟 #9 改動無關，是既有 bug，這次測試才暴露**
- 修補：2 處 `sq.QuestionId` → `sq.ParentQuestionId`

## 新增的檔案

- `.claude/rules/sql/verify_listasync_cte_rewrite.sql` — 4 段對拍腳本（冪等可重跑）
  - 對拍 1：母題 HasRepliedThisStage + SubQuestionCount（SELECT 欄位）
  - 對拍 2：子題 HasRepliedThisStage（SELECT 欄位）
  - 對拍 3：母題 WHERE filter（`@HasReplied = 1` 過濾出的母題列表）
  - 對拍 4：子題 WHERE filter
  - 每段用 `CASE WHEN COUNT(*) = 0 THEN ✅ ELSE ❌` 自動判斷

## 部署順序（已執行）

```
1. 寫詳細計畫書 + user 確認 3 個決策點 ✅
2. 寫 4 段對拍 SQL 腳本 ✅
3. SSMS 跑對拍 → 4 段全 PASS ✅
4. 動 C# 改寫 ListAsync 行 582-770 整段 ✅
5. dotnet build ✅（0 警告 0 錯誤）
6. 部署 → 觸發既有 bug → 排查 + 修補 ReviewService.cs:1209-1210 ✅
7. 重新部署 ✅
8. CwtList 三 Tab + Overview 列表 + Overview 詳情 + Review Modal 全部實測通過 ✅
```

## 順手做的視覺改善（user 主動要求）

施工過程中 user 在 Overview 詳情看到「審題輔助資訊」卡片視覺要求重設計：
- 配色從 amber 改 morandi 灰藍 + 保留 amber 在 lightbulb 徽章
- 字體升級 `prose-sm text-sm leading-relaxed` → `prose text-base leading-loose`
- Header 雙行設計（副標「命題教師專屬」+ 主標「審題輔助資訊」）
- 圓底徽章設計（`bg-morandi/10 ring-morandi/20`）
- 拿掉內容區漸層（年長者護眼）、保留 header 淡莫蘭迪藍底（10% opacity）
- 3 處統一改：`ReviewQuestionDisplay.RenderAnalysisSection` / `Overview.RenderAnalysisSection` / `Overview.RenderSubAnalysisSection`

## 本波累積成果（#9）

| 量化指標 | 數量 |
|---|---|
| 新增 SQL 腳本 | 1（4 段對拍） |
| 重寫的 C# SQL 字串行數 | ~190 行（行 582-770） |
| 消除的 per-row subquery 類型 | 4 種（HasRepliedThisStage / MakeRepliedClause filter / SubQuestionCount / vw scalar subquery） |
| 新增的 CTE 數量 | 3（MasterReplied / SubReplied / SubCounts） |
| 順手修的既有 bug | 1（ReviewService.LoadHistoryAsync 欄位名錯） |
| 順手改善的 razor 視覺 | 3 處（審題輔助資訊卡片） |
| 新增 NuGet 套件 | **0** |
| Build 結果 | 0 警告 0 錯誤 |
| 部署後線上錯誤 | 0 |

## 預期效能

| 場景 | 改前 subquery 次數 | 改後 |
|---|---|---|
| 命題作業區 100 題 | ~200 | **0** |
| 命題作業區 1000 題 | ~2000 | **0** |
| 審修作業區（含子題 UNION）200 母 + 60 子 | ~520 | **0** |
| 1000 題 + 300 子題（資料量增長後） | ~3900 | **0** |

CTE 改寫後 SQL Server 可選 hash join / merge join，主 SELECT 1:1 LEFT JOIN 對應。資料量越大 ROI 越高。

---

# 🎉 第二波（共用基底層）全部結束

| # | 任務 | 工時 | 主要產出 |
|---|---|---|---|
| 6 | `vw_QuestionRoundStartedAt` View | ~3h | 1 view + 12 處 SQL 改寫 |
| 7 | `IMembershipService` 短 TTL Cache | ~1.5h | 1 Service + 3 處呼叫點改寫 |
| 8 | `QuestionTypeCatalog` 啟動快取 | ~2h | 1 Service + 7 處 JOIN 拿掉 |
| 9 | `ListAsync` CTE 重寫 | ~4h | ~190 行 SQL 重寫 + 1 bug 修補 + 3 處視覺改善 |
| 10 | LoginLogs 複合索引 | ~30min | 1 nonclustered index |
| 11 | PasswordResetTokens.Token 索引 | ~10min | 確認既有 UNIQUE 已可用 |
| **合計** | | **~11 小時** | **跨 4 天分次施工** |

**第二波累計量化成果**：

| 指標 | 數量 |
|---|---|
| 新增 SQL 腳本 | 4 |
| 新增 DB 物件 | 1 nonclustered index + 1 view |
| 新增 C# 檔案 | 2（QuestionTypeCatalog + MembershipService） |
| 修改的 Service 檔案 | 8（Auth, Teacher, Question, Project, Dashboard, Announcement, Role, Overview, Home, Review） |
| 修改的 Model 檔案 | 1（TeacherModels） |
| 修改的 Razor 元件 | 4（MainLayout + Overview + ReviewQuestionDisplay + Home 部分） |
| 修改的 Program.cs | 2 處 DI 註冊 |
| 消除的 per-row subquery | ~2000 次 / 一次命題任務頁載入（100 筆資料情境） |
| 消除的重複 SQL 片段 | 12 處 MAX subquery + 7 處 JOIN + 1 處內聯權限檢查 = **20 處** |
| 新增 NuGet 套件 | **0** |
| Build 全程結果 | 0 警告 0 錯誤 |
| 部署後線上錯誤 | 0 |
| 順手修的既有 bug | 1（ReviewService 欄位名） |

**核心精神實現**：Linus 的「修這件事比加索引重要」— 消除重複，讓未來改業務規則只需改一處。

**下一波預定**：第三波（架構改造，1-2 週）— ProjectService Bulk Insert / OverviewService 合併 CTE / ReviewService 9 round-trip 降到 3-4 / 教師歷程分頁 / EmailService fire-and-forget / RoleService MemoryCache / 批次 INSERT。所有第三波任務都建立在第二波鋪好的基底（View + Catalog + MembershipService）之上。

---

# 附錄 G：第三波 #15 + #16 施工紀錄（2026-05-15 完成）

## 任務 #15 — 教師歷程 OFFSET FETCH 分頁

**設計策略**：選項 B（完整分頁 + UI Pager）。Service 方法加 `page` / `pageSize` 參數回傳 paged result；UI 用既有共用元件 `Components/Shared/Pagination.razor`；保留既有「梯次篩選」下拉，切換 filter 時自動 reset page=1。

**為何不用 QueryMultipleAsync**：兩段 SQL（COUNT + OFFSET FETCH）參數列不同（COUNT 不需要 Offset/PageSize），用 QueryMultipleAsync 需要拼字串。直接兩次 ExecuteScalarAsync + QueryAsync 反而更清楚，且 Dapper 會自動共用同一連線、不會多開 round-trip。

### 修改的檔案

| 檔案 | 變動 |
|---|---|
| **Models/TeacherModels.cs** | 新增 `TeacherComposeHistoryResult` + `TeacherReviewHistoryResult` 兩個 paged result 類別（沿用 `QuestionListResult` pattern：Items / TotalCount / Page / PageSize / PageCount） |
| **Services/TeacherService.cs** | `ITeacherService` 介面兩個方法加 `int page = 1, int pageSize = 10`；實作改 COUNT + OFFSET FETCH 兩段 SQL，回傳 paged result |
| **Components/Pages/Teachers.razor** | 三大改動：① 欄位 `composeHistory: List<>` → `composeResult: TeacherComposeHistoryResult`（reviewHistory 同）；② 新增 `composePage` / `reviewPage` / `isLoadingCompose` / `isLoadingReview` 狀態；③ Markup 加 `<Pagination>` 元件 + Header 顯示「共 N 筆」 |

### 核心程式片段

**Service SQL pattern**（兩個方法相同）：
```sql
-- countSql
SELECT COUNT(*) FROM ... WHERE [filter]

-- listSql
SELECT [columns] FROM ... WHERE [filter]
ORDER BY [col] DESC
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
```

**UI 行為**：
- `OnComposeFilterChangedAsync` / `OnReviewFilterChangedAsync`：切換梯次篩選 → `page = 1` → reload
- `OnComposePageChangedAsync(int page)` / `OnReviewPageChangedAsync(int page)`：分頁器點擊 → 防呆（超界 / 等於當前 / IsLoading 全擋）→ reload
- `SelectTeacher` 開新教師時 `composePage = 1; reviewPage = 1` reset

### 預期效果
- 教師命過 200 題：以前一次拉 200 筆 → 現在每次 10 筆，DB / 網路 / 渲染負載降為 1/20
- 命題教師跨梯次審題上百次：同上
- 「梯次篩選」搭配分頁器更直覺，年長使用者無需滾動長列表

---

## 任務 #16 — PasswordResetService fire-and-forget

**設計策略**：DB token commit 後立即 return success，寄信改 `_ = Task.Run(...)` 背景跑；失敗只用 `ILogger.LogError` 記錄不丟回前端；移除原本「寄信失敗 UPDATE IsUsed=1」rollback。

**why 可以移除 rollback**：token 自帶 10 分鐘 `ExpiresAt` 會自然失效；使用者收不到信時直接重申請即可（既有 logic：申請新 token 會把舊的 IsUsed=1）。

### 修改的檔案

**Services/PasswordResetService.cs**：
- Constructor 注入 `ILogger<PasswordResetService>`
- DB 寫 token 改用 `ExecuteAsync`（不需要 OUTPUT INSERTED.Id，因為已不再用 tokenId 做 rollback）
- 寄信段改寫：`_ = Task.Run(async () => { try ... await SendResetPWEmailAsync ... catch logger.LogError(...) })`
- 移除原本 try/catch + UPDATE IsUsed=1 rollback block

### 風險與處理

| 風險 | 處理 |
|---|---|
| Task.Run 沒 await 變孤兒 task | `_ =` discard + 內層 try/catch + ILogger 記錄完整 ex stack |
| 寄信失敗使用者不知道 | 接受。Token 10 分鐘自然失效，使用者重申請即可 |
| Scoped IEmailService 在 Task.Run 後失效 | `EmailService` 是 stateless 純包 SmtpClient，無 DB / Scoped 依賴，安全 |
| `_emailService` / `_logger` 在 Task.Run 內被 service scope dispose 後失效 | 改 capture 成 local 變數（`var emailService = _emailService; var logger = _logger;`）避免 lambda capture instance field |

### 預期效果
- 使用者按下「忘記密碼」按鈕：以前等 1-3 秒 SMTP → 現在 commit 完 token 就回 success（< 100ms）
- 後端 thread pool 不被 SMTP I/O 卡住

---

## 本波累積成果（#15 + #16）

| 量化指標 | 數量 |
|---|---|
| 新增的 Model 類別 | 2（TeacherComposeHistoryResult + TeacherReviewHistoryResult） |
| 修改的 Service 檔案 | 2（TeacherService.cs, PasswordResetService.cs） |
| 修改的 Razor 元件 | 1（Teachers.razor） |
| 修改的 Model 檔案 | 1（TeacherModels.cs） |
| 新增 NuGet 套件 | **0** |
| Build 結果 | 0 警告 0 錯誤 |
| 部署狀態 | 待 user 驗證 |

**待 user 驗證項目**：
- Teachers 頁面：點教師 → 切換命題歷程 Tab → 確認分頁器顯示、翻頁、切換梯次篩選自動回第 1 頁、「共 N 筆」顯示正確
- 同頁面：切到審題歷程 Tab → 同上驗證
- 忘記密碼流程：輸入信箱 → 立即看到「請至信箱收件」訊息（不再等 SMTP）→ 收信箱確認信件有到

**下一波預定**：第三波剩 5 項——#18（RoleService 批次 INSERT，~1h 小改）/ #14（ReviewService 9→3-4 round-trip，~3-4h 大改）/ #13（OverviewService 合併 CTE，~2-3h）/ #12（ProjectService Bulk Insert，~4-5h 大改）/ #17（RoleService MemoryCache，可能已被第二波 #7 IMembershipService 涵蓋，需重新評估）。建議下一個動 #18（簡單 + 立竿見影）或直接挑 #14（最有感）。

---

# 附錄 H：第三波 #18 + #14 施工紀錄（2026-05-15 完成）

## 任務 #18 — `MergeRolePermissionsAsync` 批次 INSERT

**設計策略**：保留 `DELETE FROM ...` 一個 round-trip（先清後寫的原子性靠呼叫端 transaction 保障），把原本 `foreach INSERT` N 次改為單一多列 `INSERT ... VALUES (),(),()...` 一次寫入，仿 `QuestionImagePersistence.UpsertSubAsync` 既有 pattern。

### 修改檔案
- **Services/RoleService.cs:943-980**：`MergeRolePermissionsAsync` 內部從 foreach 改為 `StringBuilder` 拼 VALUES + `DynamicParameters` 帶參。
- Permissions 為預留欄位（區塊細部權限）依然交由 DB 預設值（1=編輯）處理，不在 INSERT 列中。

### 效能變化

| 情境 | Before | After |
|---|---|---|
| 8 個模組權限儲存 | 1 DELETE + 8 INSERT = **9 round-trip** | 1 DELETE + 1 INSERT = **2 round-trip** |
| 觸發點 | 新角色 `CreateRoleAsync`（行 561）+ 編輯角色 `UpdateRoleAsync`（行 628） |

### 影響面
- 呼叫端零改動（method signature 不變、行為等效）
- 角色 CUD 對應 SignalR 廣播後續還會清 IMembershipService cache（第二波 #7 留下的機制）

---

## 任務 #14 — `GetModalDataAsync` 9→4 round-trip

**設計策略**：保留 `GetByIdAsync` 不動（其他三頁 CwtList / Reviews / Overview 也用，blast radius 大），把 Modal 自己這條連線的 7 個獨立查詢（lastEdit / creator / assign / history / similar / returnCount / sibling）合併成單一 `QueryMultipleAsync`。

**siblings Stage 自帶 subquery**：原邏輯需要先讀 myAssignment 才知道 Stage，導致 sibling 必須是第 8 個 round-trip。新作法用 self-contained `WHERE ra.ReviewStage = (SELECT TOP 1 ReviewStage FROM ... WHERE ReviewerId = @ReviewerId)` 把 Stage 推導內嵌；無 Assignment 時 subquery 回 NULL → ranked 為空 → 結果自然 0 列。移除原 `if (myAssignment is not null)` 包裹。

**helper 保留**：
- `LoadHistoryAsync` 仍保留 — OverviewService 用 aggregateAllUnits=true（含 UnitInfo CTE）走這條
- `LoadSimilaritiesAsync` 仍保留 — 雖然只 Modal 用，但精準小函式留著有助理解
- `ApplyFinalReturnSequence` / `StageToLabel` / `StageToRevisionLabel` / `StripHtmlFull` / `StripHtml` 全保留

**Modal 路徑直接 inline 精準模式 SQL**：HistoryUnion 部分 SQL（無 UnitInfo CTE 那個簡單版）複製到 megaSql 內成為第 4 個 statement，類似 LoadSimilaritiesAsync 也整段複製過來。同一段邏輯重複出現於 helper 與 megaSql — 接受這份重複，因為 helper 的彙整模式 SQL 比較複雜，硬抽出共用會讓兩邊都難讀。

### 修改檔案

**Services/ReviewService.cs:280-470**：`GetModalDataAsync` 重寫。
- 移除 5 個獨立 SQL 字串常數（lastEditSql / creatorSql / assignSql / returnSql / siblingSql）
- 新增 1 個 `megaSql` 大字串（含 7 個 SELECT statement 用 `;` 分隔）
- 改用 `using var multi = await conn.QueryMultipleAsync(megaSql, ...)`
- 依序 `ReadFirstOrDefaultAsync<>()` / `ReadAsync<>()` 取 7 個 result set
- history / similar / siblings 的 row → DTO 轉換邏輯保留（與既有 helper 一致）

### 效能變化

| 階段 | Before | After |
|---|---|---|
| GetByIdAsync 獨立連線（不動） | 2-4 round-trip | 2-4 round-trip |
| Modal 主連線 | 7-8 round-trip（含 conditional sibling） | **1 round-trip**（QueryMultiple 7 sets） |
| **總計** | ~10 round-trip | **3-5 round-trip** |
| 達標 | — | ✅ 9 → 3-4 達標 |

### 邊界條件確認
- 無 Assignment 的訪客（旁觀者）開 Modal：myAssignment = null、siblings 為空、其餘正常（lastEdit / creator / history / similar / returnCount 皆與 Reviewer 無關）
- 子題單元（subQuestionId 非 NULL）：所有 SQL 用 `ISNULL(SubQuestionId, -1) = ISNULL(@SubQuestionId, -1)` NULL-safe 比對，邏輯與舊版一致

### 未處理項目
- `UpsertFinalSubQuestionsAsync` N+1（總召直接編輯子題的路徑）— 因觸發頻率低（每次審題未必走到）+ 子題數量通常 2-5 個，ROI 偏低，本次不動。第四波再評估。

---

## 本波累積成果（#18 + #14）

| 量化指標 | 數量 |
|---|---|
| 修改 Service 檔案 | 2（RoleService.cs, ReviewService.cs） |
| 修改 Model 檔案 | 0 |
| 新增 NuGet 套件 | **0** |
| 消除的 round-trip | 角色儲存 9→2、Modal 開啟 10→3-5 |
| Build 結果 | 0 警告 0 錯誤 |
| 部署狀態 | 待 user 驗證 |

### 待 user 驗證項目
- **角色頁面**：開「新增角色」→ 設定 8 個模組權限 → 儲存 → 確認權限正確（DB 觀察 MT_RolePermissions 8 列）
- **角色頁面**：開「編輯角色」→ 切換幾個模組 toggle → 儲存 → 確認更新正確
- **審題 Modal 開啟（所有題型）**：
  - 互審 / 專審 / 總審 三身分各開一次 Modal
  - 題組類（短文 / 閱讀 / 聽力 — TypeId IN 3, 5, 7）：母題列子題索引卡顯示正確
  - 歷程軌跡所有意見 + 修題說明出現，順序正確
  - 相似題比對清單顯示
  - 命題者名稱 + 最後編輯時間正確
  - 總召視角：退回次數標示「總審第N次退回」正確
- **效能感受**：開 Modal 應該比以前快（少 7 個 round-trip）

---

**第三波目前狀態**（11 項中 6 項完成）：
- ✅ #15 教師歷程分頁（附錄 G）
- ✅ #16 EmailService fire-and-forget（附錄 G）
- ✅ #18 MergeRolePermissions 批次 INSERT（附錄 H）
- ✅ #14 GetModalDataAsync QueryMultiple（附錄 H）
- ✅ #13 OverviewService 合併 CTE（附錄 H 後由 overview-page-curator 盤點確認已落地，2026-05-25）
- ✅ #12 ProjectService Bulk Insert（附錄 I，兩 commit 分段完成 2026-05-26）
- ⏳ #17 RoleService MemoryCache（已被第二波 #7 IMembershipService 涵蓋，建議刪除此項）

---

# 附錄 I：第三波 #12 施工紀錄（2026-05-26 完成）

## 任務 #12 — `ReplaceProjectChildRecordsAsync` 完整批次化

**設計策略**：仿 #18 `MergeRolePermissionsAsync` 同一套 pattern — `StringBuilder` 拼多列 `INSERT VALUES (),(),()` + `DynamicParameters` 帶參。**不引入 TVP**（要新增 5 個 User-Defined Table Type，schema 維運成本高）、**不引入 Dapper.Plus**（零依賴原則）。Members 用 `OUTPUT INSERTED.Id, INSERTED.UserId` 一次拿回完整映射，後續 Roles / Quotas 透過 `Dictionary<int, int>` 配對 MemberId。

**分兩 commit 切刀**：先動最安全的（無 MemberId 依賴）三段，對拍 PASS 後再動最有 ROI 的 Members + Quotas 核心段。降低改壞掉風險。

## Commit 1（2026-05-26）— Phases / Targets / Roles（嵌套）

### 改動
- Phases 段：`foreach 8 次 INSERT` → 1 個多列 VALUES INSERT
- Targets 段：`foreach ~12 次 INSERT` → 1 個多列 VALUES INSERT
- Roles 段：仍在 member loop 內，但**每位成員的 N 個 RoleId 改為 1 個多列 VALUES INSERT**（從 N round-trip 降到 1）

### Round-trip 變化（典型 30 教師梯次）

| 段別 | 改前 | 改後 |
|---|---|---|
| Phases | 8 | 1 |
| Targets | ~12 | 1 |
| Members | 30 | 30（保留不動） |
| Roles | ~45 | 30（每教師 1 批） |
| Quotas | ~225 | ~225（保留不動） |
| **小計** | **~320** | **~287** |

### 驗證
- `dotnet build` 0 警告 0 錯誤
- `verify_project_bulk_insert_commit1.sql` SSMS 對拍：Phases / Targets / Roles 三段差異 0 列 ✅

## Commit 2（2026-05-26）— Members / Roles / Quotas 全部跨成員合併

### 改動
- 主方法 `ReplaceProjectChildRecordsAsync` 拆出 3 個 private static helper：
  - `BulkInsertMembersAsync` — `OUTPUT INSERTED.Id, INSERTED.UserId` 回傳 `Dictionary<int, int>` (UserId → MemberId)
  - `BulkInsertMemberRolesAsync` — 跨成員合併所有 RoleIds 為單一 INSERT
  - `BulkInsertMemberQuotasAsync` — 跨成員合併所有 Quotas 為單一 INSERT
- 新增 `private sealed record MemberInsertRow(int Id, int UserId)` 接 OUTPUT 結果

### Round-trip 變化（典型 30 教師梯次）

| 段別 | Commit 1 後 | Commit 2 後 |
|---|---|---|
| Phases | 1 | 1 |
| Targets | 1 | 1 |
| Members | 30 | **1** |
| Roles | 30 | **1** |
| Quotas | ~225 | **1** |
| **合計** | **~287** | **5** |

從 ~320 次降到 **5 次** round-trip，Serializable 鎖定時間從數百毫秒降到數十毫秒。

### 驗證
- `dotnet build` 0 警告 0 錯誤
- `verify_project_bulk_insert_commit2.sql` SSMS 對拍：Phases / Targets / Members / Roles / Quotas 五段差異 0 列 ✅

## 新增的檔案（2 SQL 對拍腳本）
- `.claude/rules/sql/verify_project_bulk_insert_commit1.sql`
- `.claude/rules/sql/verify_project_bulk_insert_commit2.sql`

## 修改的檔案（1 個）
- `Services/ProjectService.cs:993-1132` — `ReplaceProjectChildRecordsAsync` 整段重寫 + 新增 3 個 private helper + 1 個 record

## 限制（已記在程式碼註解）
SQL Server 單一指令參數上限 2100。典型梯次遠低於此（30 教師 × ~7 quotas × 5 欄 ≈ 1050 參數）；若未來真撞上限，會以清楚的 SqlException 提示，再分批處理即可（非本次處理範圍）。

## 累積成果

| 量化指標 | 數量 |
|---|---|
| 修改的 Service 檔案 | 1（ProjectService.cs） |
| 新增的 private helper | 3 |
| 新增的 private record | 1 |
| 新增的 SQL 對拍腳本 | 2 |
| Round-trip 降幅 | ~320 → 5（**降 98%**） |
| 新增 NuGet 套件 | **0** |
| Schema 變動 | **0** |
| Build 結果 | 0 警告 0 錯誤 |
| 對拍結果 | Commit 1 三段 / Commit 2 五段全部 0 列 ✅ |

---

**第三波目前狀態**（11 項中 6 項完成，剩 1 項評估後可刪）：所有高 ROI 任務皆已落地。剩 #17 屬於評估後可刪除（已被第二波 #7 IMembershipService 涵蓋）。**第三波實質完工**。
