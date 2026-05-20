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
- `ClearFieldError(fieldName)`：使用者修改欄位時即時移除單欄錯誤（只有 Remove 成功才 StateHasChanged）
- 訊息為空字串時 = 僅顯示紅框，不顯示欄位下方文字（帳密錯誤情境：帳號密碼兩欄皆紅框但無訊息）
- `BuildLoginInputClass(fieldName, includeLeftIconPadding, extraClasses, fullWidth)` 根據 `fieldErrors.ContainsKey` 動態切換 `border-red-400` vs `border-gray-300`

## 驗證順序

1. 帳號/密碼空白 → 各自顯示欄位紅字，`errorMessage` 顯示「請輸入完整的帳號密碼」
2. 驗證碼錯誤 → 記錄 LoginLog（失敗）、清空驗證碼、重新產生、顯示欄位紅字
3. `ValidateLoginAsync` 失敗 → 帳號密碼欄皆紅框（無訊息）、錯誤 banner 顯示 Service 回傳訊息

## 開發模式

- `IsDev = Env.IsDevelopment()`（注入 `IWebHostEnvironment`）
- 初始化時自動填入：`Username = "jay"`，`Password = "01024304"`，`CaptchaInput = generatedCaptcha`
- 顯示「開發模式」提示條（emerald 色）
- 例外錯誤訊息會顯示完整 Exception 類型與訊息（生產環境改為通用文字）

## 已登入導向

- `OnInitializedAsync` 檢查 `AuthenticationState`，已登入時：
  - 若 Cookie 有 `IsFirstLogin=true` Claim → `Navigation.NavigateTo("first-login-password", replace: true)`
  - 否則 → `Navigation.NavigateTo(string.Empty, replace: true)`（導至根路由 `/`）
  - **replace: true** 避免在 browser history 多塞一筆，防止使用者按上一頁繞回

## 表單模型

`LoginFormModel` 定義在 `Models/AuthModels.cs`（非 Razor 內嵌 class）：
- `Username`：帳號或信箱（支援兩者）
- `Password`：密碼
- `CaptchaInput`：驗證碼輸入
（無 RememberMe 欄位：已於 2026-05-15 移除，改為一律 Session Cookie，關瀏覽器即失效）

首次登入、重設密碼模型也在同一個 `Models/AuthModels.cs` 檔案中：
- `FirstLoginPasswordFormModel`：NewPassword + ConfirmPassword
- `ResetPasswordFormModel`：NewPassword + ConfirmPassword

## 其他 UI 細節

- h1 加上 `tabindex="-1"` 和 `focus:outline-none`（避免 FocusOnNavigate 藍框）
- 密碼眼睛按鈕使用 Font Awesome `fa-eye` / `fa-eye-slash`
- 資安提示區塊（blue-50）固定顯示在驗證碼下方
- 頁腳顯示 `@DateTime.Now.Year 全民中文檢定 CWT`
- 驗證碼圖片尺寸：`h-[42px] w-[140px]`，click 觸發 `RefreshCaptcha()`

## 首次登入強制改密碼（FirstLoginPassword.razor）

- 路由 `/first-login-password`，需已登入（無 `[AllowAnonymous]`，使用 Cookie 驗證身份）
- 從 `AuthenticationState` 取 `ClaimTypes.NameIdentifier` 得到 userId
- 呼叫 `ResetService.ChangePasswordAsync(userId, newPassword)`
- 新舊密碼相同 → `samePasswordError = true` → 欄位下方紅字提示（無 SweetAlert）
- 密碼長度 < 6 → SweetAlert warning
- 兩次不一致 → SweetAlert error
- 成功 → SweetAlert success (timer=1000, showConfirmButton=false) → 等 1100ms → `auth/logout` 強制重新登入

## 重設密碼（ResetPassword.razor）

- 路由 `/resetpassword?token=xxx`，`[AllowAnonymous]`
- `OnInitializedAsync` 呼叫 `ResetService.ValidateTokenAsync(token)` 驗證 token
- token 無效 → `OnAfterRenderAsync` 顯示 SweetAlert warning → 導回 `/login`（避免 SSR 期間 JS 呼叫失敗）
- Token 驗證通過才顯示重設表單
- `ResetPasswordAsync` 在 Transaction 內再次驗證 token（防止併發）
- 成功 → SweetAlert success (timer=1000) → 等 1100ms → 導回 `/login`
