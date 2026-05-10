---
name: 登入認證流程細節
description: Login.razor → AuthService 完整登入流程、Cookie 設定、鎖定邏輯、UserAgent 取得方式
type: project
---

## 認證流程（Blazor 無法直接寫 Cookie 的繞道機制）

1. `Login.razor` 的 `HandleLogin()` 先呼叫 `AuthService.ValidateLoginAsync(username, password, browserUserAgent)`
2. 驗證成功後呼叫 `AuthService.PrepareSignIn(user, rememberMe)` 暫存資料並取得一次性 key（有效期 60 秒）
3. 用 `Navigation.NavigateTo($"auth/login?key={loginKey}", forceLoad: true)` 觸發 HTTP request
4. `Program.cs` 的 `/auth/login` endpoint 呼叫 `CompleteSignInAsync(key, httpContext)` 寫 Cookie
5. `CompleteSignInAsync` 完成後：清除暫時鎖定、寫 LoginLog（成功）、更新 LastLoginAt、寫 AuditLog (Login)
6. `IsFirstLogin == true` → 導向 `/first-login-password`；否則導向 `/`（根路由）

**Why:** Blazor Server render 期間沒有 HttpResponse，必須強制 load 觸發真正的 HTTP request。

## Cookie 設定

- RememberMe=true：`IsPersistent=true`，`ExpiresUtc = UtcNow + 90 天`，`AllowRefresh=false`
- RememberMe=false：`IsPersistent=false`，`ExpiresUtc=null`（由 Program.cs 的 ExpireTimeSpan=2h 控制），`AllowRefresh=true`（滑動視窗）

## 登入帳號支援

- `MT_Users.Username` 或 `MT_Users.Email` 皆可登入（SQL 查詢用 OR 條件，兩欄皆有 UNIQUE 過濾索引）

## UserAgent 取得

- `Login.razor` 在 `OnAfterRenderAsync(firstRender)` 呼叫 `loginInterop.getUserAgent` JS Interop
- 取得值存在 `browserUserAgent` 欄位，驗證碼失敗時就帶入 `LogLoginAttemptAsync`；密碼驗證由 `ValidateLoginAsync` 帶入

## 鎖定機制

- 30 分鐘視窗內連續錯誤密碼達 3 次 → 暫時鎖定 15 分鐘（寫入 `MT_Users.LockoutUntil`）
- `MT_Users.Status=0` 停用帳號、`Status=2` 手動鎖定，兩者訊息不同
- 登入成功或 `CompleteSignInAsync` 時皆呼叫 `ClearTemporaryLockoutAsync` 清除暫時鎖定

## 登入 Log 表

- `MT_LoginLogs`：UserId, Username, IsSuccess, IpAddress, UserAgent, FailReason
- `MT_AuditLogs`：UserId, Action(byte), TargetType(byte), TargetId, IpAddress
