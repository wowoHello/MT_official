# CWT 命題工作平臺 — Blazor .NET 10 搬家總體規劃

> **文件版本**：v1.0  
> **日期**：2026-03-20  
> **目標**：將 `MT_prototype` 中的 HTML/JS 前端原型，遷移至 Blazor Server (.NET 10) 架構
> **注意事項**：此為正式環境，必須保持 Clear Code 編碼方式，能夠組件複用就要組件化，且目前已經有資料庫了，不需要再模擬延遲
> **注意事項**：h1標籤的 CSS 都要加上 focus:outline-none，避免預設選取

---

## 核心開發原則

1. **零依賴優先 (Vanilla First)** - 除非我明確要求使用特定套件，否則一律優先使用 C# 相依性注入 (Dependency Injection)，若 C# 無法實現再使用原生 JavaScript (ES6+)。
   - 禁止隨意引入第三方 npm 套件（如 jQuery、Lodash 等）。
2. **極致效能 (Performance Driven)**
   - 程式碼必須以效能最佳化為前提。
   - 不建立不需要的檔案文件或引用
   - 減少不必要的 DOM 操作，避免記憶體洩漏 (Memory Leaks)，並採用高效的演算法與資料結構。
   - 遵守 Clear Code 編碼方式，保持程式碼整潔好維護
3. **樣式規範 (Tailwind CSS Only)**
   - 所有 UI 樣式一律使用 Tailwind CSS 實作。
   - 除非 Tailwind 無法達成需求，否則禁止撰寫自訂 CSS 類別或 Inline Styles。
4. **符合規則**
   - 須符合 Blazor 設計準則（例如：常常引用的位址寫在\_Imports.razor內、檔案該放該放的地方如Services、Componets/Models等...）
   - 所有的 form 都要改為使用 Blazor 專屬的 **EditForm**
   - 所有的 form 欄位要去參照 **db.md** 資料庫資料表的設計欄位
   - 資料庫效能最優化，能存數字的方式就不要存文字，例如：不需要自定義的**等級：**0 = 初級、1 = 中級、2 = 中高級、3 = 高級、4 = 優級
5. **誠實與精確 (No Hallucination)**
   - 如果查不到相關資料、缺乏上下文，或沒有權限存取特定資訊，請直接回答「我不知道」或「我無法取得該資訊」。
   - 絕對禁止猜測、捏造 API 參數或給出模糊不清的答案。


---

## 現況盤點

### 原型參考

> MT_prototype 是 Prototype，可以用來參考樣式與功能，去重時與優化時不要刪掉

### 原型頁面清單 (12 頁)

| #   | 原型檔案                                                                           | 對應 JS                                                                       | 功能說明                          | 遷移優先  |
| --- | ---------------------------------------------------------------------------------- | ----------------------------------------------------------------------------- | --------------------------------- | --------- |
| 1   | [index.html](file:///d:/IISWebSize/MT/MT_prototype/index.html)                     | [login.js](file:///d:/IISWebSize/MT/MT_prototype/js/login.js)                 | 登入 / 忘記密碼                   | 🔴 P0     |
| 2   | [firstpage.html](file:///d:/IISWebSize/MT/MT_prototype/firstpage.html)             | [firstpage.js](file:///d:/IISWebSize/MT/MT_prototype/js/firstpage.js)         | 登入後首頁 (功能捷徑)             | 🔴 P0     |
| 3   | [dashboard.html](file:///d:/IISWebSize/MT/MT_prototype/dashboard.html)             | [dashboard.js](file:///d:/IISWebSize/MT/MT_prototype/js/dashboard.js)         | 儀表板 (統計圖表)                 | 🟡 P1     |
| 4   | [projects.html](file:///d:/IISWebSize/MT/MT_prototype/projects.html)               | [projects.js](file:///d:/IISWebSize/MT/MT_prototype/js/projects.js)           | 專案梯次管理                      | 🟡 P1     |
| 5   | [overview.html](file:///d:/IISWebSize/MT/MT_prototype/overview.html)               | [overview.js](file:///d:/IISWebSize/MT/MT_prototype/js/overview.js)           | 專案總覽 (進度+題目分佈)          | 🟡 P1     |
| 6   | [cwt-list.html](file:///d:/IISWebSize/MT/MT_prototype/cwt-list.html)               | [cwt-list.js](file:///d:/IISWebSize/MT/MT_prototype/js/cwt-list.js)           | 題目列表 (CRUD + 篩選)            | 🟠 P2     |
| 7   | [reviews.html](file:///d:/IISWebSize/MT/MT_prototype/reviews.html)                 | [cwt-review.js](file:///d:/IISWebSize/MT/MT_prototype/js/cwt-review.js)       | 審題作業                          | 🟠 P2     |
| 8   | [announcements.html](file:///d:/IISWebSize/MT/MT_prototype/announcements.html)     | [announcements.js](file:///d:/IISWebSize/MT/MT_prototype/js/announcements.js) | 公告管理                          | 🟢 P3     |
| 9   | [teachers.html](file:///d:/IISWebSize/MT/MT_prototype/teachers.html)               | [teachers.js](file:///d:/IISWebSize/MT/MT_prototype/js/teachers.js)           | 教師人才庫管理                    | 🟢 P3     |
| 10  | [roles.html](file:///d:/IISWebSize/MT/MT_prototype/roles.html)                     | [roles.js](file:///d:/IISWebSize/MT/MT_prototype/js/roles.js)                 | 角色/權限管理                     | 🟢 P3     |
| 11  | [role-login-test.html](file:///d:/IISWebSize/MT/MT_prototype/role-login-test.html) | —                                                                             | 角色登入測試頁 (DEV)              | ⚪ 不遷移 |
| —   | —                                                                                  | [shared.js](file:///d:/IISWebSize/MT/MT_prototype/js/shared.js)               | 共用邏輯 (Navbar, Auth, FontCtrl) | 🔴 P0     |

### 原型技術棧

| 技術                        | 用途            | Blazor 替代方案                |
| --------------------------- | --------------- | ------------------------------ |
| Tailwind CSS v4 (離線檔案)  | UI 樣式         | **Tailwind CSS v4** (維持原樣) |
| Font Awesome 6 (離線檔案)   | 圖示            | Font Awesome 6 (保留)          |
| SweetAlert2 (離線檔案)      | 彈窗通知        | SweetAlert2 via JS Interop     |
| Quill Editor (離線檔案)     | 富文本編輯      | Quill via JS Interop           |
| Google Fonts (Noto Sans TC) | 字體            | 保留                           |
| localStorage                | 狀態管理 (DEMO) | **EF Core + 真實 DB**          |

---

## User Review Required

> [!IMPORTANT]
> **CSS 框架轉換**：原型使用 **Tailwind CSS v4**，依據您的規則設定。所有 UI 將以 Tailwind CSS 實作，視覺風格保持一致（Morandi 色系、glassmorphism 效果等）。

> [!IMPORTANT]
> **認證機制**：原型使用 localStorage 模擬登入。Blazor 版本建議使用 **ASP.NET Core Identity** 或 **Cookie-based Authentication**（搭配資料庫 Users 表），請確認偏好的認證方案。

> [!WARNING]
> **遷移範圍**：此規劃為**完整遷移路線圖**，實際執行時建議**一次處理一個頁面**，每完成一個頁面即做驗證後再進入下一頁。請問是否同意此漸進式遷移策略？

---

## 擬定架構

### 專案結構 (Blazor Server) -基礎結構

> 若元件能組件化管理，務必使用組件化方式，不要全部寫在一個頁面
> 組件化工具建立在 Components/Shared 內

```
MT/
├── Components/
│   ├── App.razor                    # 根元件
│   ├── Routes.razor                 # 路由設定
│   ├── _Imports.razor               # 全域 using
│   ├── Layout/
│   │   ├── MainLayout.razor         # [改] 主版面 (含 Navbar)
│   │   ├── LoginLayout.razor        # [新] 登入專用版面 (無 Navbar)
│   │   └── ...
│   ├── Pages/
│   │   ├── Login.razor              # 登入頁
│   │   ├── FirstPage.razor          # 首頁
│   │   ├── Dashboard.razor          # 儀表板
│   │   ├── Projects.razor           # 專案管理
│   │   ├── Overview.razor           # 總覽
│   │   ├── CwtList.razor            # 題目列表
│   │   ├── Reviews.razor            # 審題
│   │   ├── Announcements.razor      # 公告管理
│   │   ├── Teachers.razor           # 教師管理
│   │   ├── Roles.razor              # 角色管理
│   │   └── NotFound.razor           # 404
│   └── Shared/                       # [新] 共用小元件
│       ├── ProjectSwitcher.razor    # 專案切換器
│       ├── FontController.razor     # 字體縮放
│       ├── ConfirmDialog.razor      # 確認對話框 (取代 SweetAlert 部分)
│       └── QuillEditor.razor        # Quill 編輯器包裝
├── Data/
│   ├── AppDbContext.cs              # [新] EF Core DbContext
│   └── Entities/                    # [新] 28 個 Entity Model
│       ├── Role.cs
│       ├── User.cs
│       ├── Teacher.cs
│       ├── Project.cs
│       ├── ...
│       └── AuditLog.cs
├── Services/                         # 商業邏輯服務層
│   ├── AuthService.cs               # 認證服務
│   ├── ProjectService.cs            # 專案服務
│   ├── QuestionService.cs           # 題目服務
│   └── ...
├── wwwroot/
│   ├── css/
│   │   └── all.min.css                  # FontAwesome 6
│   │   └── tailwind.css        # tailwind CSS
│   │   └── quill.snow.css        # quill編輯器 CSS
│   ├── js/
│   │   ├── sweetalert2@11.js    # SweetAlert2
│   │   └── quill.js         # Quill JS
│   │   └── login-interop.js         # 登入頁面 JS
│   ├── lib/
│   │   └── bootstrap/               # Bootstrap 5 (存在但不使用)
│   ├── webfonts                     # FontAwesome 6 靜態檔案
│   └── images/                       # 靜態圖片
├── Program.cs                        # 應用程式進入點
├── appsettings.json                  # 設定檔
└── MT.csproj                         # 專案檔
```

---

## Proposed Changes

### Phase 1：基礎建設

#### [MODIFY] [MT.csproj](file:///d:/IISWebSize/MT/MT.csproj)

- 新增 NuGet 套件：
  - `Microsoft.EntityFrameworkCore.SqlServer`
  - `Microsoft.EntityFrameworkCore.Tools`
  - `Microsoft.AspNetCore.Authentication.Cookies` (或 Identity)

#### [MODIFY] [appsettings.json](file:///d:/IISWebSize/MT/appsettings.json)

- 新增 SQL Server 連線字串 `ConnectionStrings:DefaultConnection`

#### [MODIFY] [Program.cs](file:///d:/IISWebSize/MT/Program.cs)

- 註冊 `DbContext`、`Authentication`、`Services`

#### [MODIFY] [App.razor](file:///d:/IISWebSize/MT/Components/App.razor)

- 引入 Font Awesome 6 離線檔案
- 引入 Google Fonts (Noto Sans TC)
- 引入 SweetAlert2 離線檔案
- 引入 Quill 離線檔案
- 引入 Tailwind CSS 離線檔案

#### [MODIFY] [app.css](file:///d:/IISWebSize/MT/wwwroot/app.css)

- 共用樣式

---

### Phase 2：資料層 (Data Layer)

#### [NEW] Data/Entities/\*.cs

- 依據 [implementation_plan.md](file:///d:/IISWebSize/MT/MT_prototype/PRD/implementation_plan.md) 的 28 表定義建立 C# Entity 類別
- 使用 Data Annotations 或 Fluent API 設定關聯、索引、約束

#### [NEW] [AppDbContext.cs](file:///d:/IISWebSize/MT/Data/AppDbContext.cs)

- 定義所有 `DbSet<T>`
- `OnModelCreating` 中設定 Fluent API 配置
- Seed Data 初始化 (Roles, Modules, QuestionTypes)

---

### Phase 3：共用元件

#### [MODIFY] [MainLayout.razor](file:///d:/IISWebSize/MT/Components/Layout/MainLayout.razor)

- 使用 Tailwind CSS + 固定頂部 Navbar
- 整合 ProjectSwitcher、使用者資訊、登出功能
- 保留 Morandi 配色與視覺風格

#### [NEW] [LoginLayout.razor](file:///d:/IISWebSize/MT/Components/Layout/LoginLayout.razor)

- 登入頁專用 Layout（無 Navbar、無 Sidebar）

#### [NEW] Components/Shared/ProjectSwitcher.razor

- 從 [shared.js](file:///d:/IISWebSize/MT/MT_prototype/js/shared.js) 的 [initProjectSwitcher()](file:///d:/IISWebSize/MT/MT_prototype/js/shared.js#129-226) 遷移為 Blazor 元件
- 下拉選單 + 搜尋 + 群組顯示

#### [NEW] Components/Shared/FontController.razor

- 從 [shared.js](file:///d:/IISWebSize/MT/MT_prototype/js/shared.js) 的 [injectGlobalFontController()](file:///d:/IISWebSize/MT/MT_prototype/js/shared.js#271-350) 遷移
- Speed Dial 浮動按鈕 + 拖拽功能 (需 JS Interop)

---

### Phase 4：頁面遷移 (逐頁進行)

> 每個頁面遷移包含：HTML → Razor、JS → C# + JS Interop、Tailwind

#### P0 - 登入頁

- [NEW] Pages/Login.razor — 登入表單、驗證碼、忘記密碼 Modal
- [NEW] Services/AuthService.cs — 認證邏輯

#### P0 - 首頁

- [NEW] Pages/FirstPage.razor — 功能捷徑卡片

#### P1 - 儀表板、專案管理、總覽

- [NEW] Pages/Dashboard.razor, Projects.razor, Overview.razor
- [NEW] Services/ProjectService.cs

#### P2 - 題目列表、審題

- [NEW] Pages/CwtList.razor, Reviews.razor
- [NEW] Services/QuestionService.cs, ReviewService.cs

#### P3 - 公告、教師、角色

- [NEW] Pages/Announcements.razor, Teachers.razor, Roles.razor
- [NEW] Services/AnnouncementService.cs, TeacherService.cs, RoleService.cs

---

## Verification Plan

### 建置驗證

```powershell
# 每次修改後確認專案可正常建置
cd d:\IISWebSize\MT
dotnet build
```

### 開發伺服器驗證

```powershell
# 啟動開發伺服器並在瀏覽器中驗證
cd d:\IISWebSize\MT
dotnet run
# 瀏覽器開啟 https://localhost:5001 驗證各頁面
```

### 瀏覽器測試 (每個頁面遷移後)

- 使用 browser_subagent 工具開啟頁面
- 驗證 UI 渲染正確 (Tailwind 排版、Morandi 色系)
- 驗證互動功能 (表單提交、Modal 彈窗、元件互動)
- 驗證響應式佈局 (不同視窗尺寸)

### 手動驗證 (請使用者協助)

- 確認各頁面視覺風格是否符合預期
- 確認使用者流程 (登入→首頁→各功能頁) 是否順暢
- 確認頁面切換效能是否可接受

---

## 遷移策略說明

### 漸進式遷移 (推薦)

1. **每次只遷移一個頁面**
2. 完成後立即進行建置 + 瀏覽器驗證
3. 通過驗證後再進入下一頁面
4. 如發現共用問題，優先修復後再繼續

### JS → Blazor 遷移原則

- **DOM 操作** → Razor 雙向綁定 (`@bind`, `@onclick`)
- **localStorage** → C# Service + 資料庫
- **fetch / API** → 直接呼叫 C# Service (Server-Side)
- **SweetAlert2** → JS Interop 呼叫
- **Quill** → JS Interop 包裝元件
- **事件監聽** → Blazor 事件 (`@onclick`, `@onchange`, `EventCallback`)

---

## 文檔查詢規範

- 在使用任何第三方套件前，必須先用 Context7 查詢最新文檔
