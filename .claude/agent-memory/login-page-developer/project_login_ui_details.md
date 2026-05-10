---
name: Login.razor UI 細節與欄位驗證行為
description: 登入頁面 UI 結構、欄位錯誤管理機制、開發模式行為、版面配置
type: project
---

## 版面配置

- Layout：`LoginLayout`（無 Navbar）
- 整體：左側 3/5 背景圖（`img/login-bg.png`），右側 2/5 燕麥奶白
- 登入卡片：最大寬度 `max-w-md`，靠右偏移 `md:mr-[10%] xl:mr-[15%]`
- 行動版：置中顯示，左側圖片隱藏（`hidden md:block`）

## 欄位驗證機制（非 DataAnnotations）

Login.razor **不使用 DataAnnotationsValidator**，改用自訂 `Dictionary<string, string> fieldErrors`：

- `SetFieldError(params (string Field, string Message)[] errors)`：清空所有錯誤後重新設定
- `ClearFieldError(fieldName)`：使用者修改欄位時即時移除單欄錯誤
- 訊息為空字串時 = 僅顯示紅框，不顯示欄位下方文字（帳密錯誤情境）
- `BuildLoginInputClass` 根據 `fieldErrors.ContainsKey` 動態切換 `border-red-400` vs `border-gray-300`

## 驗證順序

1. 帳號/密碼空白 → 各自顯示欄位紅字，`errorMessage` 顯示「請輸入完整的帳號密碼」
2. 驗證碼錯誤 → 記錄 LoginLog（失敗）、清空驗證碼、重新產生、顯示欄位紅字
3. `ValidateLoginAsync` 失敗 → 帳號密碼欄皆紅框（無訊息）、錯誤 banner 顯示 Service 回傳訊息

## 開發模式

- `IsDev = Env.IsDevelopment()`
- 初始化時自動填入：`Username = "jay"`，`Password = "01024304"`，`CaptchaInput = generatedCaptcha`
- 顯示「開發模式」提示條（emerald 色）
- 例外錯誤訊息會顯示完整 Exception 類型與訊息（生產環境改為通用文字）

## 已登入導向

- `OnInitializedAsync` 檢查 `AuthenticationState`，已登入則 `Navigation.NavigateTo(string.Empty)`（導至根路由）

## 表單模型

`LoginFormModel`（sealed class，定義在 Razor @code 內）：
- `Username`：帳號或信箱（支援兩者）
- `Password`：密碼
- `CaptchaInput`：驗證碼輸入
- `RememberMe`：記住登入，預設 false

## 其他 UI 細節

- h1 加上 `tabindex="-1"` 和 `focus:outline-none`（避免 FocusOnNavigate 藍框）
- 密碼眼睛按鈕使用 Font Awesome `fa-eye` / `fa-eye-slash`
- 資安提示區塊（blue-50）固定顯示在驗證碼下方
- 頁腳顯示 `@DateTime.Now.Year 全民中文檢定 CWT`
