---
name: 登入認證流程細節
description: Login.razor → AuthService 完整登入流程、Cookie 設定、鎖定邏輯、LoginLog/AuditLog 表
type: project
---

## 認證流程（Blazor 無法直接寫 Cookie 的繞道機制）

1. `Login.razor` 的 `HandleLogin()` 先呼叫 `AuthService.ValidateLoginAsync(username, password, browserUserAgent)`
2. 驗證成功後呼叫 `AuthService.PrepareSignIn(user, rememberMe)` 暫存資料並取得一次性 key（有效期 60 秒，超過視為過期）
3. 用 `Navigation.NavigateTo($"auth/login?key={loginKey}", forceLoad: true)` 觸發 HTTP request
4. `Program.cs` 的 `/auth/login` endpoint 呼叫 `CompleteSignInAsync(key, httpContext)` 寫 Cookie
5. `CompleteSignInAsync` 完成後：清除暫時鎖定 → 寫 LoginLog（成功，EventType=1）→ 更新 LastLoginAt
6. `IsFirstLogin == true` → 導向 `/first-login-password`；否則導向 `/`（根路由，`string.Empty`）

**Why:** Blazor Server render 期間沒有 HttpResponse，必須強制 load 觸發真正的 HTTP request。

## Cookie 設定

- RememberMe=true：`IsPersistent=true`，`ExpiresUtc = UtcNow + 90 天`，`AllowRefresh=false`
- RememberMe=false：`IsPersistent=false`，`ExpiresUtc=null`（由 Program.cs 的 `ExpireTimeSpan=2h` 控制），`AllowRefresh=true`（滑動視窗）

## Cookie Claim 清單

`CompleteSignInAsync` 寫入以下 Claim：
- `ClaimTypes.NameIdentifier` = `user.Id.ToString()`
- `ClaimTypes.Name` = `user.Username`
- `"DisplayName"` = `user.DisplayName`
- `ClaimTypes.Role` = `user.RoleName`
- `"RoleId"` = `user.RoleId.ToString()`

## 登入帳號支援

- `MT_Users.Username` 或 `MT_Users.Email` 皆可登入（SQL 查詢用 OR 條件：`WHERE u.Username = @Input OR u.Email = @Input`，兩欄皆有 UNIQUE 過濾索引）

## UserAgent 取得

- `Login.razor` 在 `OnAfterRenderAsync(firstRender)` 呼叫 `loginInterop.getUserAgent` JS Interop
- 取得值存在 `browserUserAgent` 欄位，驗證碼失敗時就帶入 `LogLoginAttemptAsync`；密碼驗證由 `ValidateLoginAsync` 帶入

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
- 欄位：UserId（失敗時可能 null）、Username、ProjectId（登入時 NULL）、EventType、IsSuccess、IpAddress、UserAgent、FailReason

## 防枚舉設計

`RequestPasswordResetAsync` 無論信箱是否存在，回傳訊息完全相同（避免洩漏帳號是否存在）
