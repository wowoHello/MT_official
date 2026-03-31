# RBAC 角色與權限映射到 Claims 指南

在使用 Cookie Authentication 時，將自定義資料庫中的角色 (Roles) 和權限 (Permissions) 映射到使用者的 Claims 是實現基於角色的存取控制 (RBAC) 的關鍵步驟。

## 1. 為什麼要映射到 Claims？

Claims 是 .NET 認證系統中表示使用者身份和屬性的標準方式。透過將角色和權限轉換為 Claims，您可以直接利用 .NET 內建的授權機制（如 `[Authorize(Roles = "...")]` 或 `[Authorize(Policy = "...")]`），而不需要在每個需要權限檢查的地方手動查詢資料庫。

## 2. 映射流程

### 2.1 資料庫結構假設

假設您有以下自定義資料表：
- `MT_Users`: 儲存使用者資訊 (Id, Username, PasswordHash)
- `MT_Roles`: 儲存角色資訊 (Id, RoleName)
- `MT_UserRoles`: 關聯使用者和角色 (UserId, RoleId)
- `MT_Permissions`: 儲存權限資訊 (Id, PermissionName)
- `MT_RolePermissions`: 關聯角色和權限 (RoleId, PermissionId)

### 2.2 在登入時獲取並映射 Claims

在使用者成功驗證帳號密碼後，您需要從資料庫中獲取其關聯的角色和權限，並將它們轉換為 `Claim` 物件。

```csharp
// LoginService.cs 中的 LoginAsync 方法片段

// 1. 驗證使用者 (假設已成功，取得 user 物件)
var user = await _userRepository.ValidateUserAsync(username, password);

// 2. 從資料庫獲取使用者的角色和權限
var roles = await _roleRepository.GetRolesByUserIdAsync(user.Id);
var permissions = await _permissionRepository.GetPermissionsByUserIdAsync(user.Id); // 根據角色獲取權限

// 3. 建立 Claims 列表
var claims = new List<Claim>
{
    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
    new Claim(ClaimTypes.Name, user.Username),
    // 可以添加其他使用者屬性，如 Email 等
};

// 4. 映射角色到 Claims
foreach (var role in roles)
{
    // 使用 ClaimTypes.Role 是 .NET 識別角色的標準方式
    claims.Add(new Claim(ClaimTypes.Role, role.RoleName));
}

// 5. 映射權限到 Claims (可選，如果需要更細粒度的控制)
foreach (var permission in permissions)
{
    // 使用自定義的 Claim Type，例如 "Permission"
    claims.Add(new Claim("Permission", permission.PermissionName));
}

// 6. 建立 ClaimsIdentity 和 ClaimsPrincipal
var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
var principal = new ClaimsPrincipal(identity);

// 7. 發放 Cookie
await _httpContextAccessor.HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
```

## 3. 在 Blazor 中使用 Claims 進行授權

### 3.1 基於角色的授權

如果您只映射了角色，可以使用 `Roles` 屬性進行授權：

- **在元件中**：
  ```razor
  <AuthorizeView Roles="Admin, Manager">
      <Authorized>
          <p>只有 Admin 或 Manager 可以看到。</p>
      </Authorized>
  </AuthorizeView>
  ```

- **在頁面路由上**：
  ```razor
  @page "/admin-panel"
  @attribute [Authorize(Roles = "Admin")]
  ```

### 3.2 基於策略 (Policy) 的授權 (適用於權限)

如果您映射了具體的權限（例如 "CanEditPost"），建議使用策略授權。

首先，在 `Program.cs` 中定義策略：

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireEditPostPermission", policy => 
        policy.RequireClaim("Permission", "CanEditPost"));
});
```

然後，在 Blazor 中使用該策略：

- **在元件中**：
  ```razor
  <AuthorizeView Policy="RequireEditPostPermission">
      <Authorized>
          <button>編輯文章</button>
      </Authorized>
  </AuthorizeView>
  ```

- **在頁面路由上**：
  ```razor
  @page "/edit-post/{id}"
  @attribute [Authorize(Policy = "RequireEditPostPermission")]
  ```

透過這種方式，您可以將自定義的資料庫 RBAC 模型完美整合到 .NET 的標準認證授權體系中。
