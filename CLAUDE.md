# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

# CWT 命題工作平臺 — Blazor Server (.NET 10)

> **專案性質**：正式環境（已連接資料庫，非 Demo）。
> **核心功能**：命題任務與審題任務，提供命題教師與審題專家方便的命審工具；所有資訊以「專案梯次」分類，並提供管理員儀表板監管。
> **使用者輪廓**：多為年長者 → 注重功能性、效能與易讀性。
> **配色**：莫蘭迪色系（晨光書房）。

---

## 核心開發原則（所有改動都受這些原則約束）

1. **零依賴優先 (Vanilla First)** — 一律優先用 C# DI；C# 做不到才用原生 ES6+ JS。禁止隨意引入 jQuery、Lodash 等第三方 npm 套件（資安類成熟函式庫如 HtmlSanitizer 屬例外）。
2. **極致效能** — 不建立不需要的檔案、不寫無謂 DOM 操作、避免 memory leak。資料庫已存在，不要模擬延遲。
3. **Tailwind CSS Only** — 全站樣式一律 Tailwind v4 utility class；除非 Tailwind 無法達成，禁止自訂 CSS class 或 inline style。**字體最小 `text-xs`**，不可用 `text-[10px]` 等過小字體（使用者多為年長者）。
4. **三檔案規則（最重要）** — 每個頁面拆成三個檔案：
   - `Components/Pages/{Name}.razor`（UI）
   - `Services/{Name}Service.cs`（商業邏輯與 DB 存取）
   - `Models/{Name}Models.cs`（DTO / Enum）
5. **EditForm 與資料庫對齊** — 表單一律用 Blazor `EditForm`；能存數字（enum/tinyint）絕對不存文字（例：等級 0=初級、1=中級…）。
6. **誠實 (No Hallucination)** — 查不到、缺上下文、沒權限就直說「我不知道」。禁止猜 API 參數或捏造答案。
7. **不要隨意新增功能** — 優化往簡化方向走，不要越優化越龐大。除非明確要求，否則不新增功能、不新增 DB 欄位/索引/類別（動工前先評估必要性）。
8. **Blazor 設計準則** — 常用 using 寫在 `Components/_Imports.razor`；元件能組件化就拆到 `Components/Shared/`，不要全塞進單一 razor。
9. **`<h1>` 必須加 `focus:outline-none`**（避免 `FocusOnNavigate` 預設藍框）。
10. 不常改動的文字用 Model 管理，不需特地建資料表（種子字典如 `MT_QuestionTypes` 也不加 `IsActive` 等動態欄位）。
11. **改頁面前先提計畫，使用者同意才動工**；commit 前 `dotnet build` 0 警告 0 錯誤。

> `wwwroot/css/tailwind.css` 是編譯產物，**不要手改**；要改樣式改 `wwwroot/css/input.css` 後重新編譯。

---

## 常用指令

```powershell
# 建置（每次改完必跑；0 警告 0 錯誤才算完成）
dotnet build

# 本機執行
dotnet run

# 編譯 Tailwind CSS（改完 wwwroot/css/input.css 後）
npm run build:css     # 一次性 minify 輸出 tailwind.css
npm run watch:css     # 開發時監看
```

- **沒有獨立測試專案**：以 `dotnet build` + 瀏覽器手動驗證為最低門檻。UI 改動需實際開頁面驗證 Tailwind 排版、Modal、表單流程、響應式佈局。
- **資料庫查詢**：需要看 DB 資料時，把 SQL 提供給使用者，由使用者執行後回傳結果（截圖）。
- **NPOI 授權**：`.csproj` 已含 `<AcceptNPOIOSMFLicense>true</AcceptNPOIOSMFLicense>`（NPOI 2.8 OSMF 模式，年營收 < 1 萬美元免費），缺這行 build 會被擋。

---

## 技術棧

| 類別 | 採用 |
| --- | --- |
| Runtime | **.NET 10** |
| UI | **Blazor Server**（`AddInteractiveServerComponents` + `InteractiveServer` rendermode） |
| 資料存取 | **Dapper + Microsoft.Data.SqlClient**（**非 EF Core**） |
| 即時同步 | **SignalR**（`/hubs/projects` ProjectsHub） |
| 認證 | **Cookie Authentication**（非 Identity）+ `IHttpContextAccessor`；密碼 **PBKDF2** 雜湊 |
| 快取 | `IMemoryCache`（角色權限、題型字典、階段轉換去重） |
| 樣式 | **Tailwind CSS v4** + 莫蘭迪色系 |
| 富文本 | **Quill** via JS Interop |
| 對話框 | **SweetAlert2** via JS Interop |
| 圖表 | **ApexCharts** via JS Interop |
| HTML 消毒 | **HtmlSanitizer (Ganss.Xss)** — 寫入端防 Stored XSS |
| Excel | **NPOI** — 教師匯入匯出、結案資料匯出 |
| 字體/圖示 | Noto Sans TC + Font Awesome 6（離線） |

JS interop 檔位於 `wwwroot/js/`（`login-interop.js`、`quill-interop.js`、`swal-interop.js`、`apex-interop.js`、`font-controller.js`、`annotation.js` 等）。

---

## 高層架構

### 啟動流程（`Program.cs`）
1. 註冊 `RazorComponents` + `SignalR` + `Cookie Auth`（Session Cookie 為主，`ExpireTimeSpan=24h` 滑動延長作安全網，無「記住登入」）。
2. 註冊應用程式服務（見下方服務清單）。
3. **啟動時 `await IQuestionTypeCatalog.ReloadAsync()`**：把 7 種題型字典載入記憶體（fail-fast，DB 連不上就不啟動，避免帶空資料上線）。
4. Pipeline：`UsePathBase`（讀 `Configuration["PathBase"]`，IIS 子應用程式設 `/MT`、本機留空）→ `UseStatusCodePagesWithReExecute("/not-found")` → Auth/Authorization/Antiforgery → `UseStaticFiles` + `MapStaticAssets`（前者服務 runtime 上傳檔、後者服務發佈期既有檔，需並存）→ `MapRazorComponents<App>` → `MapHub<ProjectsHub>`。
5. HTTP Endpoints（`Endpoints/`）：
   - `/auth/login`、`/auth/logout`（Razor 元件不能直接寫 Cookie，必須走 HTTP request）
   - `/api/upload`（Quill 圖片）、`/api/upload-audio`（聽力音檔）→ 寫 `wwwroot/uploads/`
   - `/api/appointment-cert/upload`、`/api/appointment-cert/zip/{projectId}`（聘書）→ 寫 `wwwroot/files/`

### 應用程式服務
**頁面對應 Service（大致一頁一服務）**：`ICaptchaService`、`IAuthService`、`IEmailService`、`IPasswordResetService`、`IProjectService`、`IRoleService`、`IAnnouncementService`、`ITeacherService`、`IQuestionService`、`IReviewService`、`IOverviewService`、`IHomeService`、`IDashboardService`、`ISystemLogService`、`IUserGuideService`、`IRevisionService`、`ISimilarityService`、`IAppointmentService`、`IAnnotationService`。

**跨頁共用基底（消除重複 SQL / 集中邏輯）**：
- `IDatabaseService` — Dapper 連線工廠（讀 `appsettings.json` 的 `ConnectionStrings:DefaultConnection`）。
- `IMembershipService` — 使用者「effective 角色 + 模組權限」查詢，30 秒短 TTL cache；角色/權限變動時主動 invalidate。
- `IQuestionTypeCatalog`（Singleton）— 7 種題型字典常駐記憶體，全站 SQL 不再 JOIN `MT_QuestionTypes`。
- `IPhaseTransitionCoordinator`（Singleton）— 階段自動轉換入口，60 秒去重，CwtList / Reviews / OverviewService 共用。
- `IHtmlSanitizationService`（Singleton）— 寫入端富文本消毒（防 Stored XSS）。

### 頁面 ↔ Service ↔ Model

| 頁面 | 主要 Service | Model | 路由 / 用途 |
| --- | --- | --- | --- |
| `Login.razor` | Auth + Captcha + PasswordReset + Email | — | `/login`、忘記密碼 |
| `FirstLoginPassword` / `ResetPassword` | PasswordReset | — | 首次登入強制改密 / Token 重設 |
| `Home.razor` | Home | HomeModel | `/` 首頁（功能捷徑、今日提醒、公告、手冊預覽） |
| `Dashboard.razor` | Dashboard | DashboardModels | 命題儀表板 KPI |
| `Projects.razor` | Project（+Appointment） | ProjectModels | 專案梯次 CRUD（SignalR 同步）、結案 Excel 匯出 |
| `Overview.razor` | Overview（內含 Question / Revision / Annotation） | OverviewModels | `/overview` 命題總覽（七階段燈號） |
| `CwtList.razor` | Question（+Similarity） | QuestionModels | 命題任務（7 題型、配額、3 Tab） |
| `Reviews.razor` | Review（+Annotation） | ReviewModels | 審題任務（互審/專審/總審 + 歷史） |
| `Announcements.razor` | Announcement（+UserGuide） | AnnouncementModels | 系統公告 CRUD + 使用說明手冊管理 |
| `Teachers.razor` | Teacher | TeacherModels | 教師人才庫 + 批次匯入 + 跨梯次歷程 |
| `Roles.razor` | Role | RoleModels | 帳號管理 + 角色權限矩陣 |
| `SystemLogs.razor` | SystemLog | — | `/system-logs` 全站活動記錄 |
| `RevisionHistory.razor` | Revision | — | `/revision-history` 審後修訂紀錄 |

### 認證流程
- Razor 元件無 `HttpResponse` 不能直接寫 Cookie，故：`Login.razor` 呼叫 `AuthService.PrepareSignIn` 暫存 ClaimsPrincipal → `NavigateTo("/auth/login?key=...", forceLoad:true)` 觸發 HTTP request → `/auth/login` endpoint 才 `CompleteSignInAsync` 寫 Cookie。首次登入導 `/first-login-password`，否則導 `/`。
- 一律 Session Cookie（`IsPersistent=false`）：關瀏覽器即失效；`ExpireTimeSpan=24h` + 滑動延長作安全網。
- 密碼 PBKDF2 雜湊；登入比對 `WHERE Username = @Input OR Email = @Input`（帳號或信箱皆可登入）。
- 登入/登出寫 `MT_LoginLogs`（`EventType` 1=Login / 2=Logout）；資料 CUD 寫 `MT_AuditLogs`。

### 稽核紀錄（`MT_AuditLogs`）兩條鐵律
1. **`ProjectId` 欄位**：跨梯次活動（人員/角色/教師/專案/公告 CUD）一律 NULL；梯次內活動（試題/審題 CUD）才填當前梯次 Id。
2. **`OldValue` / `NewValue`**：必須是 JSON（用 `AuditLogJsonHelper.Serialize`），且**包含 `targetDisplayName`**——目標被刪除後 SystemLogs / Dashboard 靠它 fallback 顯示名稱。Delete 把名稱寫 OldValue，Create/Update 寫 NewValue。

**活動呈現分流**：
- 全站活動（登入登出、人員/角色/教師/專案/公告 CUD）→ `SystemLogs.razor`，資料源 `MT_LoginLogs` + `MT_AuditLogs WHERE ProjectId IS NULL`。
- 梯次內活動（試題/審題 CUD）→ `Dashboard.razor`，資料源 `MT_AuditLogs WHERE ProjectId = @pid`。

### 路由與授權（`Components/Routes.razor` + `_Imports.razor`）
- 預設全站 `[Authorize]`；登入/重設密碼頁 `[AllowAnonymous]` + `@layout LoginLayout`。
- 未授權渲染「自動導回登入頁」+ `RedirectToLogin` 元件；找不到頁面導 `/not-found`。
- **外部教師（命題教師、審題委員）不能進公告/專案/教師/角色管理頁**——首頁卡片、麵包屑、引導動作都不可連到這些禁止頁。

### 共用元件（`Components/Shared/`）
- 通用：`CustomModal`、`DebouncedSearchInput`、`EmptyState`、`StatusBadge`、`Pagination`、`PhaseProgressStepper`、`FontController`、`RedirectToLogin`。
- Quill 體系：`QuillEditor` / `QuillEditorHost` / `QuillField` / `InlineQuillEditor`。
- 命題：`Shared/QuestionForms/`（7 題型表單）、`Shared/QuestionPreviews/`（7 題型考卷預覽）。
- 審題/修訂：`Shared/ReviewForms/`、`Shared/RevisionForms/`。

---

## 角色與權限

8 個功能模組權限由 `RoleService` 管理、`MembershipService` 查詢（含快取）。

- **預設角色（權限不可改）**：命題教師、審題委員、總召。
- **自訂角色（可新增/編輯）**：如系統管理員、計畫主持人、教務管理者。
- **外部教師的命題/審題身分綁在「專案梯次」內**（`MT_ProjectMemberRoles`），其全站角色（`MT_Users.RoleId`）為「預設教師」。判定「使用者在某梯次的角色」用 `MembershipService.GetEffectiveRoleIdsAsync(userId, projectId)`（回「全站角色 ∪ 該梯次內角色」）。

---

## 重要業務規則

### 命題流程（七階段）
產學起迄 → **命題階段** → 交互審題 → **互審修題** → 專家審題 → **專審修題** → 總召審題 → **總召修題**。
粗體階段倒數 5 天會在首頁今日提醒紅字示警。

### 三審制度與迴避規則
- **互審**：命題教師互審，**只能給意見**（無採用/退回按鈕）；自己命的題不分配給自己。
- **專審**：專家學者，可「採用」「改後再審」，**無不採用**。
- **總審**：可「採用」「改後再審」「不採用」；**最多退回 2 次**，第 3 次由總審親自修並下最終裁決。
- **迴避**：總召若同時為專審委員，總審階段不可拿到自己專審過的題；同一人兼命題＋審題，進專審不可拿到自己互審過的題。
- **總召 Sticky**：總召判「改後再審」後，命題教師重送的題必須回到原判決的同一位總召（不重新分配）。
- **結案**：僅「採用」入庫，其餘一律不採用。

### 命題任務頁三 Tab
- **命題作業區**：草稿 / 完成 / 已送審
- **審修作業區**：鎖定審查中 / 修題中
- **審核結果與歷史**：採用 / 不採用（唯讀）

### 雙模式 CWT / LCT
`MT_Projects.ProjectType`：0=CWT、1=LCT。CWT 走「題型 × 母/子粒度」配額；LCT 走「難度一~五」。題型可見性由 `QuestionConstants` 依 ProjectType/ExamLevel 過濾；精選單選題（TypeId=2）已軟下架（`HiddenTypeIds`）。三審制度、迴避、權限兩模式共用。

### 使用說明手冊（依角色）
手冊以 `MT_UserGuideFiles.PageKey` 為槽位鍵：`login`（頁面制、匿名）+ `role:{RoleId}`（每個角色一份，動態）。上傳在 Announcements 手冊管理 Modal；使用者登入後於首頁依「當前梯次角色聯集」看到對應手冊（`UserGuideService.GetViewableAsync(userId, projectId)`）。PDF 存 `wwwroot/uploads/guides/`，點按一律新分頁內嵌預覽。

---

## 提交慣例

- 訊息用繁體中文、單一主題（例：`修復公告自動下架時間判斷`）。
- 不要把 CSS、資料庫、頁面重構混成一包。
- PR 寫變更目的、影響範圍、手動驗證步驟；UI 改動附截圖；動到設定/連線/權限請明寫風險。
