---
name: 登入認證流程細節
description: Login.razor → AuthService 完整登入流程、Cookie 設定、PBKDF2 密碼、鎖定邏輯、LoginLog 表
type: project
---

## 認證流程（Blazor 無法直接寫 Cookie 的繞道機制）

1. `Login.razor` 的 `HandleLogin()` 先呼叫 `AuthService.ValidateLoginAsync(username, password, browserUserAgent)`
2. 驗證成功後呼叫 `AuthService.PrepareSignIn(user)` 暫存資料並取得一次性 key（有效期 60 秒，超過視為過期）
3. 用 `Navigation.NavigateTo($"auth/login?key={loginKey}", forceLoad: true)` 觸發 HTTP request
4. `Program.cs` 的 `/auth/login` endpoint 呼叫 `CompleteSignInAsync(key, httpContext)` 寫 Cookie
5. `CompleteSignInAsync` 完成後：清除暫時鎖定 → 寫 LoginLog（成功，EventType=1）→ 更新 LastLoginAt
6. `IsFirstLogin == true` → 導向 `/first-login-password`；否則導向 `/`（根路由，`string.Empty`）

**Why:** Blazor Server render 期間沒有 HttpResponse，必須強制 load 觸發真正的 HTTP request。

## Cookie 設定

- 一律 Session Cookie：`IsPersistent=false`、`ExpiresUtc=null`、`AllowRefresh=true`
- 關瀏覽器即失效需重登（資安考量，不提供「記住登入」）
- Program.cs 設 `ExpireTimeSpan=24h` + `SlidingExpiration=true` 作為 cookie 外洩防護的安全網（實務上關瀏覽器就清除，不會走到此邊界）

## Cookie Claim 清單

`CompleteSignInAsync` 寫入以下 Claim：
- `ClaimTypes.NameIdentifier` = `user.Id.ToString()`
- `ClaimTypes.Name` = `user.Username`
- `"DisplayName"` = `user.DisplayName`
- `ClaimTypes.Role` = `user.RoleName`
- `"RoleId"` = `user.RoleId.ToString()`
- `"IsFirstLogin"` = `"true"` 或 `"false"`（**重要**：用於 Login.razor 的 `OnInitializedAsync` 防止已登入使用者按上一頁繞回首頁，若為 true 強制導回 `/first-login-password`）

## 登入帳號支援

- `MT_Users.Username` 或 `MT_Users.Email` 皆可登入（SQL 查詢用 OR 條件：`WHERE u.Username = @Input OR u.Email = @Input`，兩欄皆有 UNIQUE 過濾索引）

## UserAgent 取得

- `Login.razor` 在 `OnAfterRenderAsync(firstRender)` 呼叫 `loginInterop.getUserAgent` JS Interop
- `loginInterop` 定義在 `wwwroot/js/app.js`（第 77 行）；**不是獨立的 login-interop.js**（該檔案不存在）
- 取得值存在 `browserUserAgent` 欄位，驗證碼失敗時就帶入 `LogLoginAttemptAsync`；密碼驗證由 `ValidateLoginAsync` 帶入
- Pre-rendering 期間 JS Interop 會例外（無瀏覽器），用 try/catch 靜默忽略

## 鎖定機制（重要：三層）

- `Status=0`：停用帳號（訊息：「此帳號已停用，請聯繫管理員。」）
- `Status=2`：手動鎖定（訊息：「此帳號已被鎖定，請聯繫管理員。」）
- 自動暫時鎖定：30 分鐘視窗內連續錯誤密碼達 **3 次** → 暫時鎖定 **15 分鐘**（寫入 `MT_Users.LockoutUntil`）
  - 計數查詢只計算「FailReason = 帳號或密碼錯誤」且發生在最後一次成功登入之後的記錄
  - 登入成功或 `CompleteSignInAsync` 時呼叫 `ClearTemporaryLockoutAsync` 清除

## MT_LoginLogs 寫入規則

- `EventType=1`（登入）：所有嘗試皆記錄（驗證碼失敗、帳密失敗、成功皆記）
- `EventType=2`（登出）：`/auth/logout` endpoint 呼叫 `LogLogoutAsync`，反查 MT_Users 取得 Username
- **不寫 MT_AuditLogs**（登入/登出不是資料 CUD，只寫 LoginLogs）
- DB 欄位：Id、UserId（失敗時可能 null）、Username、IsSuccess、IpAddress、UserAgent、FailReason、CreatedAt、ProjectId（登入/登出時不傳，DB 預設 NULL）、EventType
- 程式碼 INSERT 語句不含 ProjectId 欄位（留給 DB DEFAULT）

## /auth/login 與 /auth/logout 端點位置

- 認證 endpoint 抽出至 `Endpoints/AuthEndpoints.cs`（`public static class AuthEndpoints`）
- `Program.cs` 透過 `app.MapAuthEndpoints()` extension 方法掛載
- `/auth/login`：接收 `key` query string → `CompleteSignInAsync` → 首次登入導 `~/first-login-password`，否則導 `~/`
- `/auth/logout`：try/catch 寫 LogLogout → `SignOutAsync` → 導 `~/login`（稽核失敗不阻擋登出）

## 防枚舉設計

`RequestPasswordResetAsync` 無論信箱是否存在，回傳訊息完全相同（避免洩漏帳號是否存在）

## 密碼雜湊機制（2026-05-15 升級，附錄 A）

- 新格式：`PBKDF2.v1$<iter>$<salt-base64>$<hash-base64>`，100,000 iterations，16 byte salt，SHA256，約 90 字元
- 舊格式（向後相容）：純 Base64 編碼的 32 byte SHA256(UTF-16LE)，約 44 字元
- `VerifyPassword()` 回傳 `(IsValid, NeedsUpgrade)`，NeedsUpgrade=true 代表走舊格式
- 登入成功且 `NeedsUpgrade=true` → `UpgradePasswordHashAsync()` 靜默升級寫回 DB（失敗不阻擋登入）
- `HashPassword()` 與 `VerifyPassword()` 為 `static` 方法，供 PasswordResetService、RoleService、TeacherService 共用呼叫
