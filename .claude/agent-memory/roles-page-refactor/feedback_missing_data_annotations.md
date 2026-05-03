---
name: EditForm 有 DataAnnotationsValidator 但缺 Data Annotation 標記
description: RoleModels.cs 的 Request 類別沒有 [Required] 等屬性，EditForm 驗證機制形同虛設，實際靠手動 if 判斷
type: feedback
---

`Roles.razor` 的兩個 EditForm 都有 `DataAnnotationsValidator`，但 `CreateAccountRequest` 和 `CreateRoleRequest` 均缺少 `[Required]`、`[StringLength]` 等 Data Annotation 屬性。

**Why:** 目前驗證邏輯全部集中在 SaveAccount()/SaveRole() 的手動 if 判斷，表單欄位不會出現 Blazor 標準的紅色邊框或 ValidationMessage 提示，使用者體驗較差。

**How to apply:**
- 下次修改 RoleModels.cs 時，在 Request 類別上補 `[Required(ErrorMessage = "xxx")]`
- 在 Roles.razor 的表單欄位後加 `<ValidationMessage For="@(() => model.Field)" />`
- Code Review 編號：Y-003（中優先級）
