# CWT/LCT 雙模式命題系統 — 完整擴充計畫書

> **計畫日期**：2026-05-21
> **作者**：Jay 與 Claude 共同設計（10 個頁面 Agent 並行研究 + 對話拍板）
> **狀態**：✅ 設計層拍板完成 + Phase 0 DB 健檢通過 — 等待 Phase 2 授權
> **影響範圍**：11 個頁面、0 個新 SQL Migration、~5 個 Service 改動
> **預估工時**：4 天分階段提交
> **TypeId 對應以 DB 為主**：1=一般單選題、2=精選單選題（已軟下架）、3=閱讀題組、4=長文題目、5=短文題組、6=聽力測驗、7=聽力題組
> **2026-05-21 修訂**：撤回原 Phase 1/6（DB IsActive 欄位過度設計），改採既有 `HiddenTypeIds=[2]` C# 端方案，總工時從 5.5 天縮減為 4 天

---

## 一、任務背景

命題系統升級為雙模式：
- **CWT（全民中文檢定）**：原有 4 題型走「題型 × 母/子粒度」配題
- **LCT（聽力中心）**：新增模式只走聽力題目/題組，按「難度一~五」配題

兩者**僅配題規則差異**，其餘三審制度、迴避邏輯、權限、流程一律共用。

---

## 二、拍板的設計決策（已確認）

### 2.1 配題規則總對照

| 維度 | CWT | LCT |
|---|---|---|
| `MT_Projects.ProjectType` | 0 | 1 |
| `MT_Projects.ExamLevel` | 0-4（必填）| NULL |
| 可命題型 TypeId | 1（一般單選題）、3（閱讀題組）、4（長文題目）、5（短文題組）| 6（聽力測驗）、7（聽力題組）|
| 配題單位 | 題型 × 母/子粒度 | 難度一~五（單題）+ 聽力題組 |
| 等級命名 | 初等/中等/中高等/高等/優等 | 難度一/二/三/四/五 |
| 母/子粒度 | 母題與子題分開計數 | 無此概念 |
| 配題等級分配 | 整梯次鎖定 ExamLevel | 命題時依難度分配 |
| 三審制度 | 互審→專審→總審 | 相同 |
| 迴避規則 | 全套 | 相同 |
| 角色權限 | 共用 8 模組 | 共用，不新增 LCT 專屬角色 |

### 2.2 精選單選題(TypeId=2) — 完全停用（已實作）

兩種模式下 **皆不可命**。既存資料軟保留（供歷史題庫檢視），新表單下拉移除。
**做法（已完成）**：`Models/QuestionModels.cs:287` `HiddenTypeIds = [2]` + `VisibleTypeIdToKey` 自動過濾。
**不採用 DB IsActive 欄位**：`MT_QuestionTypes` 設計上「永不變動」（Catalog 註解明訂），加 DB 欄位是為了不會發生的「動態管理」需求，違反「不要隨意新增功能」原則。

### 2.3 配額存法（`MT_ProjectTargets`、`MT_MemberQuotas`）

| 情境 | TypeId | Level | Granularity | TargetCount 含義 |
|---|---|---|---|---|
| CWT 一般單選題 | 1 | NULL | 0 | 單題數 |
| CWT 閱讀題組母 | 3 | NULL | 0 | 母題數 |
| CWT 閱讀題組子 | 3 | NULL | 1 | 子題數 |
| CWT 長文題目 | 4 | NULL | 0 | 單題數 |
| CWT 短文題組母 | 5 | NULL | 0 | 母題數 |
| CWT 短文題組子 | 5 | NULL | 1 | 子題數 |
| LCT 聽力測驗 | 6 | 1..5 | 0 | 該難度單題數 |
| LCT 聽力題組 | 7 | NULL | 0 | 題組數 |

**LCT 聽力題組 ≠ 單題難三/難四**：聽力題組的 2 子題雖然 `MT_SubQuestions.Level` 分別寫 3、4，但**配額計算完全獨立**，不疊加到單題的難三/難四計數。

### 2.4 完成度計算 SQL 規則

```text
對每一筆配額（quota）：
  ├─ Level 非 NULL（LCT 單題）
  │    COUNT MT_Questions WHERE CreatorId=@me
  │          AND TypeId=quota.TypeId AND Level=quota.Level AND Status≥10(採用)
  │
  ├─ TypeId=7 且 Level=NULL（LCT 聽力題組）
  │    COUNT MT_Questions WHERE CreatorId=@me AND TypeId=7 AND Status≥10
  │    ※ 用母題計數，難三/難四子題不查
  │
  ├─ Granularity=1（CWT 子題）
  │    COUNT MT_SubQuestions sq JOIN MT_Questions q ON sq.ParentQuestionId=q.Id
  │          WHERE q.CreatorId=@me AND q.TypeId=quota.TypeId AND sq.Status≥10
  │
  └─ Granularity=0 且 Level=NULL（CWT 母題或單題）
       COUNT MT_Questions WHERE CreatorId=@me
             AND TypeId=quota.TypeId AND Status≥10
```

### 2.5 不變式（系統自動把關，使用者看不到）

| 不變式 | 把關位置 |
|---|---|
| LCT 聽力題組 2 子題：第 1 題 Level=3、第 2 題 Level=4 | `QuestionService.CreateAsync` (TypeId=7) 寫死 |
| CWT 題目 `Level` = `Project.ExamLevel` | 已實作 ✅ |
| LCT 不允許 `Granularity=1` 的 ProjectTarget | `Projects.razor` 配額表單只開放 LCT 對應欄位 |
| LCT 聽力題目（單題）可命難度一~五任一 | UI 5 選項全開 |
| 採用/淘汰連動：CWT 母不採 → 子全淘 | 既有規則不變 |

---

## 三、DB Schema 部署檢核（Phase 0）

### 3.1 既存 Migration 狀態

| 檔案 | 內容摘要 | 部署狀態 |
|---|---|---|
| `sql/migrate_project_type_and_granularity.sql` | `MT_Projects.ProjectType` + `MT_ProjectTargets.Granularity` | ✅ 已執行 |
| `sql/migrate_memberquotas_granularity.sql` | `MT_MemberQuotas.Granularity` | ✅ 已執行 |
| `sql/fix_granularity_description.sql` | 修描述（0/1）| ✅ 已執行 |
| `sql/migrate_project_exam_level.sql` | `MT_Projects.ExamLevel` | ✅ 已執行（2026-05-21 健檢確認）|

**Phase 0 健檢結果（2026-05-21）**：
- ✅ A 段：5 欄位皆存在（全回 1 = TINYINT 1 byte）
- ✅ B 段：0 列不一致資料（ProjectType / ExamLevel 全部對齊）
- ✅ C 段：`MT_QuestionTypes` 7 列題型完整（TypeId 1-7）

**生產 DB 確認步驟**（請使用者協助）：
```sql
-- 確認所有欄位都到位（4 個應全部回非 0）
SELECT
  COL_LENGTH('MT_Projects', 'ProjectType')              AS T_ProjectType,
  COL_LENGTH('MT_Projects', 'ExamLevel')                AS T_ExamLevel,
  COL_LENGTH('MT_ProjectTargets', 'Granularity')        AS T_Granularity,
  COL_LENGTH('MT_MemberQuotas', 'Granularity')          AS Q_Granularity,
  COL_LENGTH('MT_MemberQuotas', 'Level')                AS Q_Level;
```

### 3.2 待新增 Migration

| 編號 | 內容 | 何時執行 |
|---|---|---|
| M2 | `MT_MemberQuotas` 加唯一索引 `(ProjectId, UserId, QuestionTypeId, Level, Granularity)`（NULL-safe）| Phase 2 開始前 |
| M3 | `MT_ProjectTargets` 同樣加唯一索引 | Phase 2 開始前 |

> M2/M3 避免 race condition 寫入重複配額列，對應 `IF (Level, Granularity) 衝突 UPDATE` 邏輯。
> ~~M1（MT_QuestionTypes 加 IsActive）~~ 已撤銷 — 既有 `HiddenTypeIds=[2]` 已達成軟下架。

---

## 四、各頁面待擴充清單

### 4.1 Projects.razor — **已完整支援，僅需驗證**（Phase 7）

實際狀態：根據 projects-razor-specialist 回報，Projects 頁面對 CWT/LCT 雙模式已完整實作（ProjectType 下拉、ExamLevel 條件顯示、配額表單分支、人員配額分配）。

**驗證項目**：
- [ ] 新增 CWT 專案：題型清單只見 4 個（無精選單選題 TypeId=2）
- [ ] 新增 CWT 專案：強制選 ExamLevel
- [ ] 新增 LCT 專案：題型清單只見 2 個
- [ ] 新增 LCT 專案：配額顯示「難度一~五 + 聽力題組」6 格獨立輸入
- [ ] 編輯既有專案：ProjectType 與 ExamLevel disabled
- [ ] 人員配額：依 ProjectType 顯示對應分配欄位

**已知技術債（不在本次處理）**：
- `ReplaceProjectChildRecordsAsync` Serializable tx + 160+ round-trip（已記於 Plan_DB_PerfReview）

### 4.2 CwtList.razor — **中度改造，最高 ROI**（Phase 2）

#### 改動清單

| 檔案 | 改動 |
|---|---|
| `Models/QuestionModels.cs:6-13` | `QuotaProgressItem` 新增 `Level int?`、`Granularity int` 兩屬性 |
| `Models/QuestionModels.cs:298-329` | `GetVisibleTypeIdToKeyForProject` 排除 TypeId=2（無論 CWT/LCT） |
| `Services/QuestionService.cs:80-110` | `GetMyQuotaProgressAsync` SQL 重寫，依 Level/Granularity 多維 GROUP BY，分四種 case 計算 Completed |
| `Components/Pages/CwtList.razor:1407-1448` | `QuotaCard` UI 重設計（CWT 母/子雙進度、LCT 五難度卡 + 題組卡）|

#### 新 SQL 結構草案

```sql
-- 讀配額列
SELECT mq.QuestionTypeId, mq.Level, mq.Granularity, mq.QuotaCount
FROM MT_MemberQuotas mq
WHERE mq.ProjectId = @ProjectId AND mq.UserId = @UserId
ORDER BY mq.QuestionTypeId,
         ISNULL(mq.Level, 99),
         mq.Granularity;
```

完成度在 C# 端依 case 分支查 `MT_Questions` 或 `MT_SubQuestions`，搭配 `QuestionTypeCatalog.GetName()` 補題型名稱。

#### QuotaCard UI 草案

**CWT 教師畫面**：
```
📝 一般單選題                0/20
📖 長文題目                  0/5
📚 短文題組   母 0/3 ▏子 0/9
📚 閱讀題組   母 0/3 ▏子 0/9
```

**LCT 教師畫面**：
```
🎧 聽力測驗（單題）
  難度一  0/3    難度二  0/3    難度三  0/2
  難度四  0/3    難度五  0/2

🎧 聽力題組（2題綁定）
  共  0/4 組
```

### 4.3 Overview.razor — **重度改造**（Phase 3）

#### 改動清單

| 檔案 | 改動 |
|---|---|
| `Components/Pages/Overview.razor:94` | 題型下拉改用 `GetVisibleTypeIdToKeyForProject(ProjectType, ExamLevel)` |
| `Services/OverviewService.cs` 多處 | `LevelLabel(int level)` 簽章改 `LevelLabel(int level, ProjectType pt)`，LCT 回「難度一~五」、CWT 回「初等/中等/中高等/高等/優等」|
| `Components/Pages/Overview.razor` 篩選/燈號/統計 | 注入 `[CascadingParameter] CurrentProject` 後依 `ProjectType` 切兩套顯示 |
| 統計卡片 | 「本梯次新增題數」「命題教師數」依 CWT/LCT 分開呈現（次要）|

### 4.4 Dashboard.razor — **輕度補強**（Phase 5）

#### 現狀
KPI 與圖表 SQL 已走 TypeId `[1,3,4,5]` / `[6,7]` 分支（dashboard-razor-curator 回報 ✅）。

#### 補強項目

| 改動點 | 說明 |
|---|---|
| 題型缺口分析卡 | CWT 應顯示「閱讀題組母 X/Y、子 A/B」雙進度；LCT 應顯示 5 難度缺口 |
| 教師落後展開 | 補 `Level`/`Granularity` 維度的目標差 |
| 形態 B 主表展開 | 保留現有「`FROM MT_QuestionTypes qt LEFT JOIN`」六處不動，但配上 `WHERE qt.IsActive=1` 排除精選題目 |

### 4.5 Teachers.razor — **中度改造**（Phase 4）

#### 改動清單

| 檔案 | 改動 |
|---|---|
| `Models/TeacherModels.cs` | `TeacherComposeItem` / `TeacherReviewItem` 加 `Level int?`、`ProjectType byte` |
| `Services/TeacherService.cs:233, 295` | 兩 SQL 補 SELECT `q.Level, p.ProjectType` |
| Teachers.razor 歷程列表 | 題型欄位 LCT 顯示「聽力測驗-難度三」複合標籤，CWT 顯示純題型名 |
| 「參與專案」Tab `TeacherProjectItem` | 加 `ProjectType`，卡片顯示 CWT/LCT 徽章 |

### 4.6 Reviews.razor — **無需改動** ✅

reviews-page-architect 確認三審制度、迴避規則、ReturnCount、PhaseCode 邏輯 CWT/LCT 完全共用，現有實作直接適用。

### 4.7 Announcements.razor — **可選改動**（暫不排程）

announcements-page-curator 提到梯次下拉是否需 CWT/LCT 分組顯示。**本次不做**，待使用者實際使用後反饋再評估。

### 4.8 Roles.razor — **無需改動** ✅

roles-page-refactor 確認 LCT 共用既有「命題教師、審題委員、總召」角色，不新增 LCT 專屬角色。

### 4.9 Home.razor — **延後處理**

home-page-maintainer 提到急件警示「配額缺口」對 LCT 計算不一定正確。**等 LCT 梯次實際上線後**再評估是否需要分支。

### 4.10 Login.razor — **無關** ✅

CWT/LCT 區分發生在登入後，與認證流程無關。

---

## 五、實作優先順序與里程碑

| Phase | 內容 | 預估 | 可獨立 commit |
|---|---|---|---|
| **Phase 0** | DB Schema 部署檢核（5 欄位驗證 + 0 異常列）| 0.5 天 | ✅ 已完成（2026-05-21）|
| ~~Phase 1~~ | ~~QuestionTypeCatalog 加 IsActive~~ | — | ❌ 撤銷 — 已用 `HiddenTypeIds=[2]` 達成 |
| **Phase 2** | **CwtList 配額系統重寫** — Model + SQL + UI（含 M2 唯一索引）| 1.5 天 | ⏳ 待授權（最高 ROI）|
| **Phase 3** | **Overview CWT/LCT 分支** — 題型下拉 + LevelLabel + 統計卡片 | 1 天 | ⏳ |
| **Phase 4** | Teachers 歷程標籤 + 徽章 | 0.5 天 | ⏳ |
| **Phase 5** | Dashboard KPI 細分（母/子 + 5 難度缺口）| 0.5 天 | ⏳ |
| ~~Phase 6~~ | ~~精選題目 IsActive 軟下架~~ | — | ❌ 撤銷 — 同 Phase 1 原因 |
| **Phase 7** | Projects 全流程實測 + 既存資料修正（若有）+ M3 唯一索引 | 0.5 天 | ⏳ |
| **合計** | | **4 天** | **5 個實作 commit**（Phase 2-5, 7）|

---

## 六、Verification Plan（每 Phase 對應）

| Phase | 驗證案例 |
|---|---|
| 0 | `SELECT COL_LENGTH(...)` 確認 5 欄位皆存在 ✅ 已通過 |
| 2 | LCT 教師登入 → CwtList 配額卡顯示「難度一~五 + 聽力題組」6 格；CWT 教師看到母/子雙進度 |
| 2 | 命 5 個 LCT 聽力題組 → 「聽力題組 0/4 → 4/4」滿；「難度三/四單題」格仍為 0/X（不疊加）|
| 2 | 新建配額重複列時撞 M2 UNIQUE 索引 |
| 3 | LCT 梯次 Overview 題型下拉只見聽力 2 項；題目列表 Level 顯示「難度三」（非「中等」）|
| 4 | 教師「命題歷程」LCT 列顯示「聽力測驗-難度三」；「參與專案」卡片顯示 CWT/LCT 徽章 |
| 5 | Dashboard 題型缺口卡 CWT 顯示母/子分項；LCT 顯示 5 難度 |
| 7 | 全流程：新增 CWT/LCT 梯次 → 加教師 + 配額 → 命題 → 三審 → 結案，無 runtime 錯誤 |
| Build | 每 Phase 結束 `dotnet build` 0 警告 0 錯誤 |

---

## 七、風險與緩解

| 風險 | 影響 | 緩解 |
|---|---|---|
| ~~既存 CWT 梯次資料無 `ExamLevel`~~ | ~~Overview/CwtList 載入失敗~~ | ✅ Phase 0 健檢 B 段確認 0 列異常，無此風險 |
| 既存 `MT_ProjectTargets` 缺 `Granularity` 列 | 配額完成度算錯 | Phase 2 補既存閱讀/短文題組的子題列（手動 INSERT 或 migration）|
| LCT 聽力題組子題 Level 未自動寫 3、4 | 將來題庫查詢出錯 | Phase 2 改 `QuestionService.CreateAsync(TypeId=7)` 寫死子題 Level |
| 既存 TypeId=2 精選單選題資料 | Overview 需仍可檢視 | 既有 `TypeIdToKey` 含全 7 項保證解碼正常；`HiddenTypeIds=[2]` 僅擋新表單下拉 |
| ~~Granularity 對統計卡片影響需釐清~~ | ~~Overview 統計可能算錯~~ | ✅ Q1 已拍板 A 案（母子各算一條，與配額對齊）|
| 跨梯次切換時 cache 殘留 | 顯示舊類型資料 | `IMembershipService` 已有 InvalidateUser 機制，必要時補 ProjectType 切換時呼叫 |

---

## 八、已拍板的決議（2026-05-21）

| # | 議題 | 結論 |
|---|---|---|
| Q1 | Overview 統計卡「本梯次新增題數」CWT 母/子題如何計數 | **A 分開**：母題與子題各算一條，與配額計算對齊 |
| Q2 | LCT 模式下教師「命題歷程」題型顯示方式 | **A 複合**：「聽力測驗-難度三」單一複合標籤 |
| Q3 | CWT/LCT 判斷依據與既存資料處理 | **採 b 案**：以 `ProjectType` 為主判斷欄位、`ExamLevel IS NULL` 為 LCT 必然結果（非判斷依據）；Phase 0 健檢 B 段確認 0 列異常，不需補預設值 |
| Q4 | LCT 聽力題組子題在 CwtList 是否可獨立檢視/編輯 | **A 可**：沿用 CWT 既有行為 |

---

## 九、後續改善（非本次範圍）

- LCT 梯次的 Home 急件警示分支（看實際使用反饋）
- Announcements 梯次下拉 CWT/LCT 分組
- `MT_QuestionTypes.Description` 補 LCT 對「難度一~五」的官方說明文字
- 題庫整合（LCT 題進「題庫系統」時與 CWT 題的對接規則）
- 跨梯次相似題比對是否需要區分 ProjectType（避免 CWT 題與 LCT 題互比，無意義）

---

## 十、相關文件

- 設計討論對話紀錄（本次 /clear 前對話）
- `Models/ProjectModels.cs:ProjectType` enum 定義
- `Models/QuestionModels.cs:GetVisibleTypeIdToKeyForProject` 既有方法
- `.claude/rules/sql/migrate_project_type_and_granularity.sql`
- `.claude/rules/sql/migrate_project_exam_level.sql`
- `.claude/rules/sql/migrate_memberquotas_granularity.sql`
- `.claude/rules/sql/fix_granularity_description.sql`
- `D:\MTrefer\MT_DB.sql`（2026-05-21 dump）

---

**設計層全部拍板、Phase 0 健檢通過。等待 Phase 1 授權實作。**

每階段完成 `dotnet build` + 自驗 + 跟您確認後才進下一階段。
