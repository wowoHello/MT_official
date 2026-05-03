# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

# CWT 命題工作平臺 — Blazor Server (.NET 10)

> **文件版本**：v2.0
> **最後更新**：2026-04-30
> **專案性質**：正式環境（已連接資料庫，非 Demo）
> **參考資源根目錄**：`D:\MTrefer\`（原型 `MT_prototype\`、規格書 `Reference_doc\`、資料庫 `db.md`、計畫書 `Task\`）
> **網站介紹**：此網站最重要的功能為命題任務與審題任務，旨在提供所有命題教師與審題專家方便的命審工具。所有資訊根據專案梯次進行分類，且提供管理員儀表板進行專案梯次資訊的監管
> **網站設計考量**：使用者多為年長者、須注重功能與效能
> **網頁配色**：莫蘭迪色系

---

## 核心開發原則（必讀，所有改動都受這些原則約束）

1. **零依賴優先 (Vanilla First)** — 一律優先使用 C# DI；C# 無法做到時才用原生 ES6+ JS。禁止隨意引入 jQuery、Lodash 等第三方 npm 套件。
2. **極致效能 (Performance Driven)** — 不建立不需要的檔案、不寫無謂的 DOM 操作、避免 memory leak。資料庫已存在，不要再模擬延遲。
3. **Tailwind CSS Only** — 全站樣式一律 Tailwind v4 utility class；除非 Tailwind 無法達成，禁止自訂 CSS class 或 Inline style。**字體最小 `text-xs`**，不可使用 `text-[10px]` 等過小字體（使用者多為年長者）。
4. **三檔案規則（最重要）** — 每個頁面必須拆成三個檔案：
   - `Components/Pages/{Name}.razor`（UI）
   - `Services/{Name}Service.cs`（商業邏輯與 DB 存取）
   - `Models/{Name}Models.cs`（DTO / Enum）
   範例：`Roles.razor` ↔ `RoleService.cs` ↔ `RoleModels.cs`
5. **EditForm 與資料庫對齊** — 所有表單一律用 Blazor `EditForm`；欄位必須對齊 `D:\MTrefer\db.md` 的資料表設計。能存數字（enum/tinyint）絕對不存文字（例：等級 0=初級、1=中級…）。
6. **誠實 (No Hallucination)** — 查不到、缺上下文、沒權限就直說「我不知道」。禁止猜 API 參數或捏造模糊答案。
7. **不要隨意新增功能** — 優化要往簡化方向走，不要越優化越龐大。除非明確要求，否則不要新增功能。
8. **每一個改動都要先寫計畫書** — 計畫書放 `D:\MTrefer\Task\{對應頁面}\`，繁體中文 Markdown，包含：計畫日期、改動內容、預期效果、可能影響、替代方案。**等使用者同意才能動工。**
9. **Blazor 設計準則** — 常用 using 寫在 `Components/_Imports.razor`，元件能組件化就拆到 `Components/Shared/`，不要全部塞進單一 razor。
10. **`<h1>` 必須加 `focus:outline-none`**（避免 `FocusOnNavigate` 預設藍框）。

---

## 常用開發指令

```powershell
# .NET
dotnet restore                  # 還原 NuGet
dotnet build                    # 提交前必跑
dotnet run                      # 本機啟動（從 Properties/launchSettings.json 取設定）

# Tailwind v4 CLI（使用 @tailwindcss/cli，編譯 input.css → tailwind.css）
npm install                     # 第一次或更新 deps
npm run build:css               # 一次性 minify 編譯
npm run watch:css               # 開發時持續監看
```

> **注意**：`wwwroot/css/tailwind.css` 是編譯產物，不要手改；改 `wwwroot/css/input.css`。

---

## 技術棧（與舊版規劃不同，請以此為準）

| 類別 | 實際採用 |
| --- | --- |
| Runtime | **.NET 10**（`global.json` 鎖 `10.0.203`） |
| UI | **Blazor Server**（`AddInteractiveServerComponents`，`InteractiveServer` rendermode） |
| 資料存取 | **Dapper 2.1.72 + Microsoft.Data.SqlClient**（**不是 EF Core**） |
| 即時同步 | **SignalR**（`/hubs/projects` ProjectsHub） |
| 認證 | **Cookie Authentication**（非 Identity）+ `IHttpContextAccessor` |
| 樣式 | **Tailwind CSS v4.2** + 莫蘭迪色系（晨光書房） |
| 富文本 | **Quill** via JS Interop（`QuillEditor.razor` / `QuillEditorHost.razor` / `QuillField.razor` / `InlineQuillEditor.razor`） |
| 對話框 | **SweetAlert2** via `js/swal-interop.js` |
| 圖表 | **ApexCharts** via `js/apex-interop.js` |
| 字體 | Google Fonts Noto Sans TC + Font Awesome 6（離線） |

JS interop 檔位於 `wwwroot/js/`：`login-interop.js`、`quill-interop.js`、`font-controller.js`、`swal-interop.js`、`apex-interop.js`。

---

## 高層架構（big picture）

### 啟動流程（`Program.cs`）
1. 註冊 `RazorComponents` + `SignalR` + `Cookie Auth`（2 小時滑動，「記住登入」於 `AuthService.CompleteSignInAsync` 改 90 天絕對期限）。
2. **註冊 13 個 Scoped Service**（每行已用註解標明所屬頁面，Service 與頁面是一對一對應）：`ICaptchaService`、`IDatabaseService`、`IAuthService`、`IEmailService`、`IPasswordResetService`、`IProjectService`、`IRoleService`、`IAnnouncementService`、`ITeacherService`、`IQuestionService`、`IReviewService`、`IOverviewService`、`IHomeService`、`IDashboardService`。
3. Pipeline：`UsePathBase`（讀 `Configuration["PathBase"]`，IIS 子應用程式用 `/MT`，本機留空）→ `UseStatusCodePagesWithReExecute("/not-found")` → Auth/Authorization/Antiforgery → `MapStaticAssets` → `MapRazorComponents<App>` → `MapHub<ProjectsHub>("/hubs/projects")`。
4. 兩個 Auth 端點（一定要走 HTTP request 才能寫 Cookie）：
   - `GET /auth/login?key=...` → `CompleteSignInAsync`，首次登入導 `/first-login-password`，否則導 `/`。
   - `GET /auth/logout` → 寫 `Logout` AuditLog（失敗不阻擋登出）→ `SignOutAsync` → 回 `/login`。
5. `POST /api/upload`：Quill 圖片上傳，限 5MB、PNG/JPEG/GIF/WebP，寫到 `wwwroot/uploads/`，回 `{pathBase}/uploads/{guid}.{ext}`。`.RequireAuthorization()` + `.DisableAntiforgery()`。

### 頁面 ↔ Service ↔ Model 對應表（11 頁）

| 頁面 | Service | Model | 路由 / 用途 |
| --- | --- | --- | --- |
| `Login.razor` | `AuthService` + `CaptchaService` + `PasswordResetService` + `EmailService` | — | `/login`、忘記密碼、寄送 token 信 |
| `FirstLoginPassword.razor` / `ResetPassword.razor` | `PasswordResetService` | — | 首次登入強制改密碼 / Token 重設 |
| `Home.razor` | `HomeService` | `HomeModel` | `/` 首頁（功能捷徑、今日提醒、公告） |
| `Dashboard.razor` | `DashboardService` | `DashboardModels` | 命題儀表板（KPI 統計） |
| `Projects.razor` | `ProjectService` | `ProjectModels` | 命題專案梯次 CRUD（與 SignalR 同步） |
| `Overview.razor` | `OverviewService`（內依賴 `IQuestionService`） | `OverviewModels` | 命題總覽（七階段燈號、跨梯次彙整） |
| `CwtList.razor` | `QuestionService` | `QuestionModels` | 命題任務（7 種題型 CRUD、配額進度、3 個 Tab） |
| `Reviews.razor` | `ReviewService` | `ReviewModels` | 審題任務（互審/專審/總審 + 結果歷史） |
| `Announcements.razor` | `AnnouncementService` | `AnnouncementModels` | 系統公告 CRUD + 自動下架 |
| `Teachers.razor` | `TeacherService` | `TeacherModels` | 教師人才庫 + 跨梯次歷程 |
| `Roles.razor` | `RoleService` | `RoleModels` + `ModulePermission` + `RoleTag` | 帳號管理 + 角色權限矩陣 |

### 共用元件（`Components/Shared/`）
- 通用：`CustomModal`、`DebouncedSearchInput`、`EmptyState`、`StatusBadge`、`PhaseProgressStepper`、`FontController`、`RedirectToLogin`。
- Quill 體系：`QuillEditor`（底部滑入面板 + 中文標點快插）、`QuillEditorHost`、`QuillField`、`InlineQuillEditor`、`SharedQuillEditorContext.cs`。
- 命題表單：`Shared/QuestionForms/` 下 7 種題型表單元件 + `QuestionAttributesSidebar` + `OptionGroup`。
- 命題預覽：`Shared/QuestionPreviews/` 下 7 種題型考卷預覽 + `QuestionPreviewModal`。
- 審題：`Shared/ReviewForms/` 下 `ReviewModal`、`ReviewActionPanel`、`ReviewDecisionBar`、`ReviewQuestionDisplay`、`ReviewHistoryTimeline`、`ReviewSimilarityBanner`。

### 資料庫連線解析（`Services/DatabaseService.cs`）
連線字串解析順序：
1. **環境變數 / IIS 設定**：`MT_SQL_Server`、`MT_SQL_Database`、`MT_SQL_UserId`、`MT_SQL_UserPassword`（四個都有值才會用，自動加 `TrustServerCertificate=true`）。
2. **fallback**：`appsettings*.json` 的 `ConnectionStrings:DefaultConnection`。

> **IIS 發佈陷阱**：`dotnet publish` 會覆蓋 `web.config`，發佈完務必重新加回 `MT_SQL_*` 四個 `<environmentVariable>`，否則站台連不上 DB。

### 認證流程細節
- Razor 元件不能直接寫 Cookie（沒有 `HttpResponse`），所以：
  1. `Login.razor` 呼叫 `AuthService.PrepareSignIn` 把 ClaimsPrincipal 暫存。
  2. 用 `NavigationManager.NavigateTo("/auth/login?key=...", forceLoad: true)` 觸發真正的 HTTP request。
  3. `Program.cs` 的 `/auth/login` endpoint 才呼叫 `CompleteSignInAsync` 寫 Cookie。
- 「記住登入」勾選後 `IsPersistent=true` 且絕對期限 90 天；未勾為 sliding 2 小時。
- 登入/登出都要寫 `MT_AuditLogs`（`AuditAction.Logout` 等）。

### 路由與授權（`Components/Routes.razor`）
- 預設全站 `[Authorize]`（在 `_Imports.razor` 設定）；登入/重設密碼頁要 `[AllowAnonymous]` + `@layout LoginLayout`。
- 未授權會渲染「自動引導回登入頁」畫面 + `RedirectToLogin` 元件。
- 找不到頁面導向 `/not-found`（`NotFound.razor`）。

---

## 角色與權限（影響 UI 顯示）

8 個功能模組的權限由 `RoleService` 管理。**外部教師（命題教師、審題委員）不能進入公告/專案/教師/角色管理頁**——任何首頁卡片、麵包屑、引導動作都不能連到這些禁止頁，否則會踩到既有規範。

預設角色（不可改權限）：命題教師、審題委員、總召。
自訂角色範例：系統管理員、計畫主持人、教務管理者。
詳見 `.claude/rules/cwt-roles-rules.md` 的權限矩陣。

---

## 重要業務規則（高頻被忽略的點）

### 命題流程（七階段）
產學起迄 → **命題階段** → 交互審題 → **互審修題** → 專家審題 → **專審修題** → 總召審題 → **總召修題**。
**粗體階段倒數 5 天會在首頁今日提醒紅字示警**。

### 三審制度與迴避規則
- **互審**：命題教師互審，**只能給意見**，沒有採用/退回按鈕；自己命的題目絕不分配給自己。
- **專審**：專家學者，可「採用」「改後再審」，**沒有不採用**。
- **總審**：可「採用」「改後再審」「不採用」；**最多退回 2 次**，第 3 次由總審親自修並下最終裁決。
- 多名總召時，若某總召在專審階段審過某題，該題進入總審必分給其他總召。
- 結案：僅「採用」入庫，其餘一律不採用。

### 命題任務頁三 Tab
- **命題作業區**：草稿 / 完成 / 已送審
- **審修作業區**：鎖定審查中 / 修題中
- **審核結果與歷史**：採用 / 不採用（唯讀）

詳細規則見 `.claude/rules/cwt-prop-rules.md`、`cwt-ex-rules.md`。

---

## `.claude/rules/` 內的規格書（每次改頁面前先讀對應檔）

| 檔案 | 對應頁面 |
| --- | --- |
| `prd-cwt-proposition-platform.md` | 全平臺 PRD（v1.3） |
| `code-style.md` | 程式碼風格 + UI/UX 規範 + 莫蘭迪配色 |
| `cwt-prop-rules.md` | 命題任務（CwtList） |
| `cwt-ex-rules.md` | 審題任務（Reviews） |
| `cwt-ac-rules.md` | 公告（Announcements） |
| `cwt-teacher-rules.md` | 教師管理（Teachers） |
| `cwt-roles-rules.md` | 角色與權限（Roles） |
| `warning_MODIFY.md` | 首頁急件提醒連結與字體控制器歷史紀錄 |

> **/compact 後**：必須重讀 `D:\MTrefer\Reference_doc\` 內的每頁任務檔案，不要遺漏。

---

## 驗證方式

- **建置**：每次修改後 `dotnet build`，編譯通過才提案完成。
- **瀏覽器**：UI 改動需用 dev-browser 工具開頁面實測，驗證 Tailwind 排版、Modal、表單流程、響應式佈局。
- **資料庫**：本機需有 SQL Server 並設定 `MT_SQL_*` 環境變數或 `appsettings.Development.json` 的 `ConnectionStrings:DefaultConnection`。
- 沒有獨立測試專案；以建置 + 手動驗證為最低門檻。

---

## 提交慣例

- 訊息用繁體中文、單一主題（例：`補上記住登入與忘記密碼功能`）。
- 不要把 CSS、資料庫、頁面重構混成一包。
- PR 需寫變更目的、影響範圍、手動驗證步驟；UI 改動附截圖；動到設定/連線/權限請明寫風險。
