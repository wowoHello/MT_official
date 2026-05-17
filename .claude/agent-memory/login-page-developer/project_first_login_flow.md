---
name: 首次登入強制改密碼流程
description: FirstLoginPassword.razor 與 ChangePasswordAsync 的設計決策、流程與防呆規則
type: project
---

## 觸發條件

`MT_Users.IsFirstLogin = 1`（tinyint/bool）時，`/auth/login` endpoint 在 `CompleteSignInAsync` 回傳 `IsFirstLogin=true` 後將導向 `/first-login-password`，而非 `/`。

## FirstLoginPassword.razor 設計重點

- 路由：`/first-login-password`；使用 `LoginLayout`（無 Navbar）
- **不是 `[AllowAnonymous]`**：需要有效 Cookie（剛完成登入），從 Cookie Claim 取得 userId 與 displayName
- 使用者身份從 `AuthState.User.FindFirst(ClaimTypes.NameIdentifier)` 取得 userId
- 顯示欄位：`DisplayName`（從 `"DisplayName"` Claim）、`Username`（只讀，供密碼管理員識別用，同時消 Chrome a11y 警告）

## 防呆規則

1. **新舊密碼不可相同**：呼叫 `PasswordResetService.ChangePasswordAsync(userId, newPassword)`，Service 內部用 `AuthService.VerifyPassword` 比對現有 hash；回傳 `SamePassword=true` → UI 顯示紅字「新密碼不可與舊密碼相同」
2. **兩次密碼需一致**：提交時比對 `NewPassword == ConfirmPassword`，不一致用 SweetAlert2 提示
3. **最短長度 6 碼**：提交時 `formModel.NewPassword.Length < 6` 用 SweetAlert2 提示

## 完成後流程

改密碼成功後 **強制登出**（`nav.NavigateTo("auth/logout", forceLoad: true)`），使用者需重新登入。

**Why：** 確保新 Cookie 不帶 `IsFirstLogin=true` Claim，防止使用者以舊 Cookie 繞過改密碼限制；`ChangePasswordAsync` 已將 `IsFirstLogin = 0` 更新回 DB。

## 防止繞過

- `Login.razor` 的 `OnInitializedAsync` 若偵測到已認證且 Cookie 有 `IsFirstLogin=true` Claim，立即 `NavigateTo("first-login-password", replace: true)` 強制導回（replace: true 避免在 browser history 多塞一筆）

## ChangePasswordAsync 共用說明

`PasswordResetService.ChangePasswordAsync` 同時被：
- `FirstLoginPassword.razor`（首次登入強制改密碼）
- 未來個人設定頁（若有）

和 `ResetPasswordAsync`（token 流程）的差異：此方法不需要 token，直接以 userId 查 DB；兩個方法共用新舊密碼防呆邏輯，都會在 UPDATE 時一併設 `IsFirstLogin = 0`。
