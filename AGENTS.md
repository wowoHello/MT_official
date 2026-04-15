# Repository Guidelines

## Project Structure & Module Organization
此專案是 `net10.0` 的 Blazor Server Web App。主要程式入口在 `Program.cs`，路由與頁面放在 `Components/`，其中 `Pages/` 是頁面、`Layout/` 是版型、`Shared/` 是可重用元件。資料模型在 `Models/`，資料存取與商業邏輯集中在 `Services/`，SignalR hub 在 `Hubs/`。靜態資源與前端腳本放在 `wwwroot/`，像是 `wwwroot/css/`、`wwwroot/js/`、`wwwroot/img/`。

## Build, Test, and Development Commands
- `dotnet restore`：還原 NuGet 套件。
- `dotnet build`：編譯整個專案，提交前至少跑一次。
- `dotnet run`：本機啟動網站，設定來自 `Properties/launchSettings.json`。
- `npm install`：安裝 Tailwind CLI 依賴。
- `npm run build:css`：將 `wwwroot/css/input.css` 編譯成 `wwwroot/css/tailwind.css`。
- `npm run watch:css`：開發時持續監看 Tailwind 樣式變更。

## Coding Style & Naming Conventions
C#、Razor、JS 皆延續現有風格：使用 4 個空白縮排，型別、元件與方法採 `PascalCase`，區域變數與私有欄位採 `camelCase`。Razor 元件檔名請與元件名稱一致，例如 `StatusBadge.razor`。服務類別維持單一職責，資料庫查詢集中在 `Services/`，不要把 SQL 或驗證流程散落到頁面元件。

## Testing Guidelines
目前 repository 沒有獨立測試專案，所以先以建置成功與手動驗證為最低門檻。修改登入、權限、密碼重設、專案列表或 SignalR 同步流程時，請至少執行 `dotnet build`，再用本機站點確認主要流程。若新增測試專案，建議使用 `ProjectName.Tests` 命名，測試名稱採 `MethodName_Scenario_ExpectedResult`。

## Commit & Pull Request Guidelines
Git 歷史以繁體中文、直接描述變更內容為主，例如 `補上記住登入與忘記密碼功能`。請用單一主題提交，避免把 CSS、資料庫與頁面重構混成一包。PR 需說明變更目的、影響範圍、手動驗證步驟；若有 UI 調整，附上截圖；若動到設定、連線或權限邏輯，請明寫風險點。

## Security & Configuration Tips
連線字串與敏感設定放在 `appsettings.*.json` 的本機或部署環境覆蓋值，不要把真實密碼提交進版控。認證、驗證碼、密碼重設與資料庫存取都已集中在 `Services/`，修改這些區塊時請優先維持既有責任邊界。
