# RBAC 實作細節

## 1. 資料庫結構

以下是基於角色的存取控制 (RBAC) 的建議資料庫結構，用於管理使用者、角色和權限。

```sql
-- Users Table
CREATE TABLE Users (
    Id INT PRIMARY KEY IDENTITY(1,1),
    Username NVARCHAR(50) NOT NULL UNIQUE,
    Email NVARCHAR(100) NOT NULL UNIQUE,
    PasswordHash NVARCHAR(255) NOT NULL,
    CreatedAt DATETIME DEFAULT GETDATE()
);

-- Roles Table
CREATE TABLE Roles (
    Id INT PRIMARY KEY IDENTITY(1,1),
    Name NVARCHAR(50) NOT NULL UNIQUE -- e.g., 'Admin', 'Editor', 'Viewer'
);

-- UserRoles (Junction Table for Many-to-Many relationship between Users and Roles)
CREATE TABLE UserRoles (
    UserId INT NOT NULL,
    RoleId INT NOT NULL,
    PRIMARY KEY (UserId, RoleId),
    FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE,
    FOREIGN KEY (RoleId) REFERENCES Roles(Id) ON DELETE CASCADE
);

-- Permissions Table
CREATE TABLE Permissions (
    Id INT PRIMARY KEY IDENTITY(1,1),
    Name NVARCHAR(100) NOT NULL UNIQUE -- e.g., 'CanCreateUser', 'CanEditProduct'
);

-- RolePermissions (Junction Table for Many-to-Many relationship between Roles and Permissions)
CREATE TABLE RolePermissions (
    RoleId INT NOT NULL,
    PermissionId INT NOT NULL,
    PRIMARY KEY (RoleId, PermissionId),
    FOREIGN KEY (RoleId) REFERENCES Roles(Id) ON DELETE CASCADE,
    FOREIGN KEY (PermissionId) REFERENCES Permissions(Id) ON DELETE CASCADE
);
```

## 2. 實作流程

### 2.1 認證 (Authentication)

使用者登入時，驗證其提供的憑證（例如使用者名稱和密碼）。成功後，建立一個包含使用者 ID 和其所屬角色的安全主體 (Security Principal)。

### 2.2 授權 (Authorization)

在 Blazor 應用程式中，可以使用 ASP.NET Core 的授權機制來實施 RBAC。這通常涉及以下步驟：

1.  **定義策略 (Policies)**：在 `Program.cs` 中定義授權策略，這些策略會檢查使用者是否具有特定的角色或權限。

    ```csharp
    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("RequireAdminRole", policy => policy.RequireRole("Admin"));
        options.AddPolicy("CanManageUsers", policy => policy.RequireClaim("Permission", "CanManageUsers"));
    });
    ```

2.  **建立自定義 Claim**：當使用者登入時，從資料庫中獲取其所屬的角色和權限，並將其作為 Claim 添加到使用者的身份中。

    ```csharp
    // 範例：在登入成功後，從資料庫獲取使用者角色和權限
    var userRoles = await _userRoleService.GetUserRolesAsync(user.Id);
    var rolePermissions = await _rolePermissionService.GetPermissionsForRolesAsync(userRoles.Select(ur => ur.RoleId));

    var claims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Name, user.Username)
    };

    foreach (var role in userRoles)
    {
        claims.Add(new Claim(ClaimTypes.Role, role.Name));
    }

    foreach (var permission in rolePermissions)
    {
        claims.Add(new Claim("Permission", permission.Name));
    }

    var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var authProperties = new AuthenticationProperties
    {
        IsPersistent = true // 保持登入狀態
    };

    await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity), authProperties);
    ```

3.  **在 Blazor 元件中使用授權**：

    - **`[Authorize]` 屬性**：用於限制對整個頁面或元件的存取。

        ```razor
        @page "/admin"
        @attribute [Authorize(Policy = "RequireAdminRole")]

        <h3>管理員頁面</h3>
        <p>只有管理員才能看到此內容。</p>
        ```

    - **`AuthorizeView` 元件**：用於根據使用者的認證和授權狀態顯示或隱藏 UI 內容。

        ```razor
        <AuthorizeView Policy="CanManageUsers">
            <Authorized>
                <p>您可以管理使用者。</p>
                <button>新增使用者</button>
            </Authorized>
            <NotAuthorized>
                <p>您沒有管理使用者的權限。</p>
            </NotAuthorized>
        </AuthorizeView>
        ```

    - **`IAuthorizationService` 服務**：在程式碼中進行細粒度的授權檢查。

        ```csharp
        @inject IAuthorizationService AuthorizationService

        @code {
            private bool canEditProduct;

            protected override async Task OnInitializedAsync()
            {
                canEditProduct = (await AuthorizationService.AuthorizeAsync(User, "CanEditProduct")).Succeeded;
            }
        }

        @if (canEditProduct)
        {
            <button>編輯產品</button>
        }
        ```

## 3. 服務層設計考量

在服務層中，應包含處理使用者、角色和權限相關業務邏輯的服務。這些服務將負責與資料庫進行互動，並提供給 Blazor 頁面或 API 端點使用。

- `UserService`: 處理使用者相關操作（新增、查詢、更新、刪除）。
- `RoleService`: 處理角色相關操作。
- `PermissionService`: 處理權限相關操作。
- `UserRoleService`: 管理使用者與角色之間的關聯。
- `RolePermissionService`: 管理角色與權限之間的關聯。

每個服務都應該注入 `IDbConnectionFactory` 或直接注入 `IDbConnection`，並使用 Dapper 執行資料庫操作。
