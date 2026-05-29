---
name: 首次登入強制改密碼流程
description: FirstLoginPassword.razor 與 ChangePasswordAsync 的設計決策、流程與防呆規則（2026-05-29 現況快照）
type: project
---

## 觸發條件

`MT_Users.IsFirstLogin = 1`（bit）時，`/auth/login` endpoint 在 `CompleteSignInAsync` 回傳 `IsFirstLogin=true` 後導向 `/first-login-password`，而非 `/`。

Cookie 中的 `"IsFirstLogin"` Claim 寫入 `"true"` 或 `"false"`（字串），用於 `Login.razor.OnInitializedAsync` 防止繞過。

## FirstLoginPassword.razor 設計重點

- 路由：`/first-login-password`；使用 `LoginLayout`（無 Navbar）
- **不是 `[AllowAnonymous]`**：需要有效 Cookie（剛完成登入），從 Cookie Claim 取得使用者資訊
- `OnInitializedAsync` 從 `AuthState.User` 取：
  - `ClaimTypes.Name` → `userName`（顯示為唯讀輸入框，供密碼管理員識別、消 Chrome a11y 警告）
  - `"DisplayName"` → `displayName`（顯示為「您好，XXX 老師」）
- 使用者 ID 在 `SaveNewPassword` 從 `AuthState.User.FindFirst(ClaimTypes.NameIdentifier)` 取得（即時讀取，非快取）
- 兩個密碼欄位各有獨立的眼睛切換按鈕（`pwdType1` / `pwdType2`）

## 防呆規則（按執行順序）

1. **兩次密碼需一致**：`formModel.NewPassword != formModel.ConfirmPassword` → SweetAlert error「兩次輸入的新密碼不同！」
2. **最短長度 6 碼**：`formModel.NewPassword.Length < 6` → SweetAlert warning「密碼長度至少 6 碼！」
3. **新舊密碼不可相同**：呼叫 `PasswordResetService.ChangePasswordAsync(userId, newPassword)`，Service 內部用 `AuthService.VerifyPassword` 比對現有 hash；回傳 `SamePassword=true` → UI 設 `samePasswordError=true` → 欄位下方顯示紅字「新密碼不可與舊密碼相同」（無 SweetAlert）
   - `ClearSamePasswordError()` 綁在 `@bind-Value:after`，使用者輸入新密碼欄時即清除

## 完成後流程

改密碼成功後：
1. SweetAlert success（`timer=1000, showConfirmButton=false`）
2. `await Task.Delay(1100)`（等動畫播完）
3. **強制登出**：`nav.NavigateTo("auth/logout", forceLoad: true)`

**Why 強制登出：** 確保新 Cookie 不帶 `IsFirstLogin=true` Claim，防止使用者以舊 Cookie 繞過改密碼限制。`ChangePasswordAsync` 已將 `IsFirstLogin=0` 寫回 DB，重新登入時取到的 Cookie 就是 false。

## 防止繞過

`Login.razor.OnInitializedAsync` 若偵測到已認證且 Cookie 有 `IsFirstLogin=true` Claim（`authState.User.HasClaim("IsFirstLogin", "true")`），立即 `Navigation.NavigateTo("first-login-password", replace: true)` 強制導回（`replace: true` 避免在 browser history 多塞一筆）。

## ChangePasswordAsync 方法位置與共用說明

定義在 `PasswordResetService.cs`（`IPasswordResetService` 介面）：
- 呼叫方：`FirstLoginPassword.razor`（首次登入強制改密碼）
- 和 `ResetPasswordAsync`（token 流程）的差異：此方法不需要 token，直接以 `userId` 查 DB
- 兩個方法共用新舊密碼防呆邏輯，都會在 UPDATE 時一併設 `IsFirstLogin=0`
- UPDATE SQL：`SET PasswordHash=@h, IsFirstLogin=0, UpdatedAt=SYSDATETIME() WHERE Id=@UserId`（無 transaction，單一 UPDATE 足夠）
