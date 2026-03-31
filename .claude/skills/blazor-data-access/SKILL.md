---
name: blazor-data-access
description: 提供 Blazor 應用程式中資料庫存取（使用 Dapper ORM）、服務層設計、依賴注入配置以及基於角色的存取控制（RBAC）資料庫結構設計的指南。適用於需要建立高效能、可維護且安全的 Blazor 資料存取解決方案的開發任務。
---

# Blazor 資料存取指南

## 概述

此技能旨在為 Blazor 應用程式提供一套標準化的資料存取方法，涵蓋從資料庫連線、資料擷取、服務層設計到安全存取控制的各個環節。透過遵循本指南，開發者可以建立高效能、易於維護且具備良好擴展性的 Blazor 應用程式。

## 核心能力

### 1. 基本連線規則

#### 1.1 資料庫連線字串

- **位置**：連線字串應儲存在 `appsettings.json` 檔案中的 `DefaultConnection` 鍵值對中。
- **安全性**：在生產環境中，應使用環境變數或 Azure Key Vault 等安全機制來管理連線字串，避免直接將敏感資訊硬編碼在設定檔中。

#### 1.2 頁面資料取用規範

- **資料存取技術 (ORM)**：建議使用 **Dapper (Micro-ORM)**，以其輕量級和高效能的特性，提供靈活的 SQL 查詢能力。
- **建立模型 (Model) 與服務層 (Service Layer)**：
    - **定義 Model**：為資料庫中的每個資料表或複雜查詢結果定義對應的 C# 模型類別。
    - **撰寫 Service**：建立專門的服務層來處理資料庫操作邏輯。每個服務應負責一個或多個相關的資料表操作，並封裝 Dapper 的使用細節。
- **效能提醒：非同步 (Async/Await)**：所有資料庫呼叫方法**務必使用非同步 (Async/Await)**。這對於 Blazor 應用程式的執行緒資源管理至關重要，能有效避免 I/O 阻塞導致伺服器效能瓶頸，提升應用程式的響應速度和可擴展性。

#### 1.3 在 Program.cs 註冊服務

將資料庫連線服務和自定義的服務層註冊到 .NET 10 的依賴注入 (Dependency Injection) 容器中。這確保了服務的可測試性、可維護性以及整個應用程式的模組化。

```csharp
// 範例：在 Program.cs 中註冊服務
builder.Services.AddTransient<IDbConnection>(sp => new SqlConnection(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IRoleService, RoleService>();
// ... 註冊其他服務
```

#### 1.4 在 Blazor 頁面注入並調用

在 Blazor 元件中，使用 `@inject` 指令引入您寫好的服務，並在元件初始化時（例如 `OnInitializedAsync` 方法中）調用服務方法來讀取資料。

```razor
@page "/users"
@inject IUserService UserService

<h3>使用者列表</h3>

@if (users == null)
{
    <p><em>載入中...</em></p>
}
else
{
    <ul>
        @foreach (var user in users)
        {
            <li>@user.Name</li>
        }
    </ul>
}

@code {
    private IEnumerable<User> users;

    protected override async Task OnInitializedAsync()
    {
        users = await UserService.GetAllUsersAsync();
    }
}
```

### 2. 資料庫結構設計 (Role-Based Access Control)

為實現基於角色的存取控制 (RBAC)，建議採用以下資料表結構設計，以清晰地定義使用者、角色和權限之間的關係：

| 資料表名稱    | 欄位                 | 說明                                     |
| :------------ | :------------------- | :--------------------------------------- |
| `Users`       | `Id`, `Username`, `PasswordHash`, `Email` | 儲存使用者基本資訊和認證憑證             |
| `Roles`       | `Id`, `Name`         | 儲存系統中定義的角色（例如：Admin, Editor, Viewer） |
| `UserRoles`   | `UserId`, `RoleId`   | 建立 `Users` 和 `Roles` 之間的多對多關係，一個使用者可以有多個角色 |
| `Permissions` | `Id`, `Name`         | 儲存系統中定義的具體權限（例如：CanCreateUser, CanEditProduct） |
| `RolePermissions` | `RoleId`, `PermissionId` | 建立 `Roles` 和 `Permissions` 之間的多對多關係，一個角色可以有多個權限 |

透過這種設計，可以靈活地管理不同使用者的權限，並在應用程式中實施精細的存取控制。

## 資源

此技能包含以下資源，可作為進一步參考：

### references/

- `dapper_best_practices.md`: Dapper ORM 的進階使用技巧和最佳實踐。
- `rbac_implementation_details.md`: RBAC 資料庫結構的詳細實作指南和範例。

### templates/

- `model_template.txt`: C# 模型類別的範本。
- `service_interface_template.txt`: 服務層介面 (Interface) 的範本。
- `service_implementation_template.txt`: 服務層實作 (Implementation) 的範本。

---

**注意**：此技能的 `scripts/` 目錄目前沒有包含任何可執行腳本，因為資料存取主要涉及程式碼撰寫和配置指南。如有需要，未來可根據具體需求添加自動化腳本。
