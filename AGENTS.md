# Repository Guidelines

## 專案結構與模組分工
`Components/` 是 Blazor UI 主體：`Pages/` 放可路由頁面，`Layout/` 放版型骨架，`Shared/` 放可重用的 Razor 元件，例如題型表單與預覽元件。`Services/` 放認證、驗證碼、密碼重設、專案流程等商業邏輯。`Models/` 放 DTO 與共用資料模型。`Hubs/` 放 SignalR hubs。`Database/Migrations/` 放手動 SQL migration scripts。靜態資源集中在 `wwwroot/`，包含 `css/`、`js/`、`img/`、`uploads/`、`webfonts/`。

## 建置、測試與開發指令
請在專案根目錄執行以下指令：

- `dotnet restore`：安裝 .NET 相依套件。
- `dotnet build`：確認 `net10.0` 的 Blazor Server 專案可成功編譯。
- `dotnet run`：啟動本機開發伺服器。
- `npm install`：安裝 Tailwind CLI 相關套件。
- `npm run build:css`：將 `wwwroot/css/input.css` 編譯成 `wwwroot/css/tailwind.css`。
- `npm run watch:css`：開發 UI 時持續監看並重建 Tailwind 樣式。

若有修改 Razor 或樣式檔，送出 PR 前至少先跑一次 `dotnet build` 與 `npm run build:css`。

## 程式風格與命名規範
遵守清楚、可拆分的小元件 Blazor 設計。C# 與 Razor 一律使用 4 個空白縮排。元件、services、公開成員使用 PascalCase，區域變數使用 camelCase，私有欄位使用 `_fieldName`。頁面只保留薄薄的頁面邏輯，可重用行為請移到 `Services/` 或 `Components/Shared/`。樣式優先使用 Tailwind utilities，只有在真的不夠用時才補到 `wwwroot/css/app.css`。

## 測試規範
目前這個 repository 還沒有獨立的測試專案。在測試專案補上之前，至少要完成 `dotnet build`、頁面 smoke test，以及登入、登出、專案切換等核心流程驗證。未來若新增測試，請建立獨立的 `*.Tests` 專案，檔名依測試目標命名，例如 `AuthServiceTests.cs`。

## Commit 與 Pull Request 規範
最近的 git 紀錄以簡短、具描述性的繁體中文 commit message 為主，請延續這個風格。建議先寫功能或模組，再寫變更目的，例如 `更新登入流程與驗證碼邏輯`。PR 內容需包含簡短摘要、影響檔案範圍、是否牽涉資料庫或 SignalR、UI 變更截圖，以及手動驗證步驟，例如 `dotnet build`、登入與登出檢查。

## 安全與設定提醒
不要提交真實的 connection strings、secrets 或正式環境郵件設定。上傳檔案規則需與 `Program.cs` 保持一致；只要改到 `/auth/*`、`/api/upload` 或 SignalR hub 行為，就要重新檢查 authorization 與存取風險。
