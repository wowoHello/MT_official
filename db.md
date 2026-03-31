-- =============================================================================
-- CWT 命題工作平臺 — 統一資料庫架構 (MSSQL)
-- 版本：完整版 (TINYINT 數值化優化 + 100% 完整擴充屬性備註 + UI邏輯防呆修正版)
-- =============================================================================

USE MT; -- DB 名稱
GO

---

## -- 1. 基礎表 (無外鍵依賴)

CREATE TABLE dbo.MT_Roles (
Id INT IDENTITY(1,1) PRIMARY KEY,
Name NVARCHAR(50) NOT NULL UNIQUE,
Category TINYINT NOT NULL,
Description NVARCHAR(500),
IsDefault BIT NOT NULL DEFAULT 0,
CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
UpdatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'定義系統中可自訂的使用者身分別', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Roles';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'唯一識別碼', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Roles', @level2type=N'COLUMN', @level2name=N'Id';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身分別名稱（可自訂）', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Roles', @level2type=N'COLUMN', @level2name=N'Name';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'角色分類 (0:內部, 1:外部)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Roles', @level2type=N'COLUMN', @level2name=N'Category';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'角色功能描述', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Roles', @level2type=N'COLUMN', @level2name=N'Description';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否為系統預設角色', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Roles', @level2type=N'COLUMN', @level2name=N'IsDefault';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'記錄建立時間', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Roles', @level2type=N'COLUMN', @level2name=N'CreatedAt';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最後修改時間', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Roles', @level2type=N'COLUMN', @level2name=N'UpdatedAt';
GO

CREATE TABLE dbo.MT_QuestionTypes (
Id INT IDENTITY(1,1) PRIMARY KEY,
Code TINYINT NOT NULL UNIQUE,
Name NVARCHAR(50) NOT NULL,
Icon NVARCHAR(50),
SortOrder INT NOT NULL DEFAULT 0
);
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'定義 7 種試題類型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_QuestionTypes';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'唯一識別碼', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_QuestionTypes', @level2type=N'COLUMN', @level2name=N'Id';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'題型代碼數值 (1:單選, 2:複選, 3:題組...)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_QuestionTypes', @level2type=N'COLUMN', @level2name=N'Code';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'題型名稱', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_QuestionTypes', @level2type=N'COLUMN', @level2name=N'Name';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'顯示圖示', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_QuestionTypes', @level2type=N'COLUMN', @level2name=N'Icon';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'排序次序', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_QuestionTypes', @level2type=N'COLUMN', @level2name=N'SortOrder';
GO

CREATE TABLE dbo.MT_Modules (
Id INT IDENTITY(1,1) PRIMARY KEY,
ModuleKey NVARCHAR(50) NOT NULL UNIQUE,
Name NVARCHAR(50) NOT NULL,
Icon NVARCHAR(50),
PageUrl NVARCHAR(100),
SortOrder INT NOT NULL DEFAULT 0,
IsActive BIT NOT NULL DEFAULT 1
);
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'定義系統的主功能模組樹', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Modules';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'唯一識別碼', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Modules', @level2type=N'COLUMN', @level2name=N'Id';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'模組識別代碼', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Modules', @level2type=N'COLUMN', @level2name=N'ModuleKey';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'模組名稱', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Modules', @level2type=N'COLUMN', @level2name=N'Name';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'選單圖示', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Modules', @level2type=N'COLUMN', @level2name=N'Icon';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'頁面路由路徑', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Modules', @level2type=N'COLUMN', @level2name=N'PageUrl';
GO

---

## -- 2. 核心使用者表

CREATE TABLE dbo.MT_Users (
Id INT IDENTITY(1,1) PRIMARY KEY,
Username NVARCHAR(100) NOT NULL UNIQUE,
DisplayName NVARCHAR(50) NOT NULL,
Email NVARCHAR(200),
PasswordHash BINARY(32) NOT NULL,
RoleId INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Roles(Id),
Status TINYINT NOT NULL DEFAULT 1,
CompanyTitle NVARCHAR(100),
Note NVARCHAR(500),
IsFirstLogin BIT NOT NULL DEFAULT 1,
LastLoginAt DATETIME2,
CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
UpdatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'儲存所有系統使用者，包含內部職員與外部教師', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Users';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'唯一識別碼', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Users', @level2type=N'COLUMN', @level2name=N'Id';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'登入帳號', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Users', @level2type=N'COLUMN', @level2name=N'Username';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'使用者姓名', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Users', @level2type=N'COLUMN', @level2name=N'DisplayName';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'電子信箱', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Users', @level2type=N'COLUMN', @level2name=N'Email';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'密碼雜湊值 (SHA2_256, 32 Bytes)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Users', @level2type=N'COLUMN', @level2name=N'PasswordHash';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所屬角色 ID', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Users', @level2type=N'COLUMN', @level2name=N'RoleId';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'帳號狀態 (0:停用, 1:啟用, 2:鎖定)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Users', @level2type=N'COLUMN', @level2name=N'Status';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'內部人員職稱', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Users', @level2type=N'COLUMN', @level2name=N'CompanyTitle';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'管理備註', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Users', @level2type=N'COLUMN', @level2name=N'Note';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否首次登入', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Users', @level2type=N'COLUMN', @level2name=N'IsFirstLogin';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最後登入時間', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Users', @level2type=N'COLUMN', @level2name=N'LastLoginAt';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'帳號建立時間', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Users', @level2type=N'COLUMN', @level2name=N'CreatedAt';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'資料更新時間', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Users', @level2type=N'COLUMN', @level2name=N'UpdatedAt';
GO

---

## -- 3. 依賴 MT_Users 的延伸表

CREATE TABLE dbo.MT_RolePermissions (
Id INT IDENTITY(1,1) PRIMARY KEY,
RoleId INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Roles(Id),
ModuleId INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Modules(Id),
IsEnabled BIT NOT NULL DEFAULT 0,
AnnouncementPerm TINYINT NOT NULL DEFAULT 1,
CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
UpdatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
CONSTRAINT UQ_MT_RolePermissions_RoleId_ModuleId UNIQUE (RoleId, ModuleId)
);
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'定義各角色對不同功能模組的存取權限', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_RolePermissions';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'唯一識別碼', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_RolePermissions', @level2type=N'COLUMN', @level2name=N'Id';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'關聯角色 ID', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_RolePermissions', @level2type=N'COLUMN', @level2name=N'RoleId';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'模組 ID', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_RolePermissions', @level2type=N'COLUMN', @level2name=N'ModuleId';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否啟用該模組', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_RolePermissions', @level2type=N'COLUMN', @level2name=N'IsEnabled';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'公告權限 (0:無, 1:檢視, 2:編輯)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_RolePermissions', @level2type=N'COLUMN', @level2name=N'AnnouncementPerm';
GO

CREATE TABLE dbo.MT_PasswordResetTokens (
Id INT IDENTITY(1,1) PRIMARY KEY,
UserId INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Users(Id),
Token NVARCHAR(500) NOT NULL UNIQUE,
RequestIp NVARCHAR(50),
ExpiresAt DATETIME2 NOT NULL,
IsUsed BIT NOT NULL DEFAULT 0,
CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'管理忘記密碼功能的驗證連結', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_PasswordResetTokens';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'唯一識別碼', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_PasswordResetTokens', @level2type=N'COLUMN', @level2name=N'Id';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'使用者 ID', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_PasswordResetTokens', @level2type=N'COLUMN', @level2name=N'UserId';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'重設識別碼', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_PasswordResetTokens', @level2type=N'COLUMN', @level2name=N'Token';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'請求來源 IP', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_PasswordResetTokens', @level2type=N'COLUMN', @level2name=N'RequestIp';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'截止時間', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_PasswordResetTokens', @level2type=N'COLUMN', @level2name=N'ExpiresAt';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否已使用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_PasswordResetTokens', @level2type=N'COLUMN', @level2name=N'IsUsed';
GO

CREATE TABLE dbo.MT_LoginLogs (
Id INT IDENTITY(1,1) PRIMARY KEY,
UserId INT FOREIGN KEY REFERENCES dbo.MT_Users(Id),
Username NVARCHAR(100) NOT NULL,
IsSuccess BIT NOT NULL,
IpAddress NVARCHAR(50),
UserAgent NVARCHAR(500),
FailReason NVARCHAR(200),
CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'記錄所有登入嘗試，用於安全稽核', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_LoginLogs';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'唯一識別碼', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_LoginLogs', @level2type=N'COLUMN', @level2name=N'Id';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'使用者 ID', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_LoginLogs', @level2type=N'COLUMN', @level2name=N'UserId';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'登入帳號', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_LoginLogs', @level2type=N'COLUMN', @level2name=N'Username';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否成功', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_LoginLogs', @level2type=N'COLUMN', @level2name=N'IsSuccess';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'客戶端 IP', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_LoginLogs', @level2type=N'COLUMN', @level2name=N'IpAddress';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'瀏覽器環境', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_LoginLogs', @level2type=N'COLUMN', @level2name=N'UserAgent';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'失敗原因', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_LoginLogs', @level2type=N'COLUMN', @level2name=N'FailReason';
GO

CREATE TABLE dbo.MT_Teachers (
Id INT IDENTITY(1,1) PRIMARY KEY,
UserId INT NOT NULL UNIQUE FOREIGN KEY REFERENCES dbo.MT_Users(Id),
TeacherCode NVARCHAR(10) NOT NULL UNIQUE,
Gender TINYINT,
Phone NVARCHAR(20),
IdNumber NVARCHAR(200),
School NVARCHAR(100) NOT NULL,
Department NVARCHAR(50),
Title NVARCHAR(20),
Expertise NVARCHAR(200),
TeachingYears INT,
Education TINYINT,
Note NVARCHAR(500),
CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
UpdatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'教師人才庫，儲存命題與審題人員的詳細背景', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Teachers';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'唯一識別碼', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Teachers', @level2type=N'COLUMN', @level2name=N'Id';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'關聯帳號 ID (1:1)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Teachers', @level2type=N'COLUMN', @level2name=N'UserId';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'教師編號 (如 T1001)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Teachers', @level2type=N'COLUMN', @level2name=N'TeacherCode';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'性別 (0:未知, 1:男, 2:女)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Teachers', @level2type=N'COLUMN', @level2name=N'Gender';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'聯絡電話', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Teachers', @level2type=N'COLUMN', @level2name=N'Phone';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身分證 (加密)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Teachers', @level2type=N'COLUMN', @level2name=N'IdNumber';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'任教學校', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Teachers', @level2type=N'COLUMN', @level2name=N'School';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'系所', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Teachers', @level2type=N'COLUMN', @level2name=N'Department';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'職稱', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Teachers', @level2type=N'COLUMN', @level2name=N'Title';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'專長', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Teachers', @level2type=N'COLUMN', @level2name=N'Expertise';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'教學年資', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Teachers', @level2type=N'COLUMN', @level2name=N'TeachingYears';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'學歷 (1:學士, 2:碩士, 3:博士)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Teachers', @level2type=N'COLUMN', @level2name=N'Education';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'人才庫備註', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Teachers', @level2type=N'COLUMN', @level2name=N'Note';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'匯入時間', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Teachers', @level2type=N'COLUMN', @level2name=N'CreatedAt';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新時間', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Teachers', @level2type=N'COLUMN', @level2name=N'UpdatedAt';
GO

CREATE TABLE dbo.MT_AuditLogs (
Id INT IDENTITY(1,1) PRIMARY KEY,
UserId INT FOREIGN KEY REFERENCES dbo.MT_Users(Id),
Action TINYINT NOT NULL,
TargetType TINYINT NOT NULL,
TargetId INT,
OldValue NVARCHAR(MAX),
NewValue NVARCHAR(MAX),
IpAddress NVARCHAR(50),
CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'系統層級稽核記錄，追蹤關鍵資料變動', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_AuditLogs';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'唯一識別碼', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_AuditLogs', @level2type=N'COLUMN', @level2name=N'Id';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'執行人 ID', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_AuditLogs', @level2type=N'COLUMN', @level2name=N'UserId';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'操作類型 (0:建立, 1:修改, 2:刪除, 3:登入, 4:登出)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_AuditLogs', @level2type=N'COLUMN', @level2name=N'Action';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'目標表類型 (0:Users, 1:Roles, 2:Projects, 3:Questions, 4:Announcements, 5:Teachers, 6:Reviews)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_AuditLogs', @level2type=N'COLUMN', @level2name=N'TargetType';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'目標資料主鍵', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_AuditLogs', @level2type=N'COLUMN', @level2name=N'TargetId';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'原始資料 (JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_AuditLogs', @level2type=N'COLUMN', @level2name=N'OldValue';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'新資料 (JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_AuditLogs', @level2type=N'COLUMN', @level2name=N'NewValue';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'來源 IP', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_AuditLogs', @level2type=N'COLUMN', @level2name=N'IpAddress';
GO

CREATE TABLE dbo.MT_UserGuideFiles (
Id INT IDENTITY(1,1) PRIMARY KEY,
FileName NVARCHAR(200) NOT NULL,
FilePath NVARCHAR(500) NOT NULL,
FileSize BIGINT NOT NULL,
UploadedBy INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Users(Id),
IsActive BIT NOT NULL DEFAULT 1
);
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'管理使用者操作手冊與相關 PDF 文件資源', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_UserGuideFiles';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'唯一識別碼', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_UserGuideFiles', @level2type=N'COLUMN', @level2name=N'Id';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'檔名', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_UserGuideFiles', @level2type=N'COLUMN', @level2name=N'FileName';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'路徑', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_UserGuideFiles', @level2type=N'COLUMN', @level2name=N'FilePath';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'大小', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_UserGuideFiles', @level2type=N'COLUMN', @level2name=N'FileSize';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'上傳者 ID', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_UserGuideFiles', @level2type=N'COLUMN', @level2name=N'UploadedBy';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'可用狀態', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_UserGuideFiles', @level2type=N'COLUMN', @level2name=N'IsActive';
GO

---

## -- 4. 專案管理群組

CREATE TABLE dbo.MT_Projects (
Id INT IDENTITY(1,1) PRIMARY KEY,
ProjectCode NVARCHAR(20) NOT NULL UNIQUE,
Name NVARCHAR(100) NOT NULL,
Year INT NOT NULL,
School NVARCHAR(100),
Status TINYINT NOT NULL DEFAULT 0,
StartDate DATE NOT NULL,
EndDate DATE NOT NULL,
ClosedAt DATETIME2,
CreatedBy INT FOREIGN KEY REFERENCES dbo.MT_Users(Id),
Description NVARCHAR(500),
IsDeleted BIT NOT NULL DEFAULT 0,
DeletedAt DATETIME2,
CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
UpdatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'管理命題梯次 (Project/Epoch) 的生命週期', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Projects';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'唯一識別碼', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Projects', @level2type=N'COLUMN', @level2name=N'Id';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'專案代碼', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Projects', @level2type=N'COLUMN', @level2name=N'ProjectCode';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'專案名稱', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Projects', @level2type=N'COLUMN', @level2name=N'Name';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'專案年度', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Projects', @level2type=N'COLUMN', @level2name=N'Year';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'合作學校', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Projects', @level2type=N'COLUMN', @level2name=N'School';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'專案狀態 (0:準備中, 1:進行中, 2:已結案)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Projects', @level2type=N'COLUMN', @level2name=N'Status';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'計畫開始日', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Projects', @level2type=N'COLUMN', @level2name=N'StartDate';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'計畫結束日', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Projects', @level2type=N'COLUMN', @level2name=N'EndDate';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'實際結案時間', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Projects', @level2type=N'COLUMN', @level2name=N'ClosedAt';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'建立人 ID', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Projects', @level2type=N'COLUMN', @level2name=N'CreatedBy';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'專案描述', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Projects', @level2type=N'COLUMN', @level2name=N'Description';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否刪除/作廢 (0:正常, 1:作廢)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Projects', @level2type=N'COLUMN', @level2name=N'IsDeleted';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'作廢/刪除時間', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Projects', @level2type=N'COLUMN', @level2name=N'DeletedAt';
GO

CREATE TABLE dbo.MT_ProjectPhases (
Id INT IDENTITY(1,1) PRIMARY KEY,
ProjectId INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Projects(Id),
PhaseCode TINYINT NOT NULL,
PhaseName NVARCHAR(50) NOT NULL,
StartDate DATE NOT NULL,
EndDate DATE NOT NULL,
SortOrder INT NOT NULL,
CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
UpdatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
CONSTRAINT UQ_MT_ProjectPhases_ProjectId_PhaseCode UNIQUE (ProjectId, PhaseCode)
);
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'定義專案內部的各個階段 (命題、三審等) 時間區間', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ProjectPhases';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'唯一識別碼', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ProjectPhases', @level2type=N'COLUMN', @level2name=N'Id';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'專案 ID', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ProjectPhases', @level2type=N'COLUMN', @level2name=N'ProjectId';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'階段代碼 (1:命題, 2:一審, 3:二審...)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ProjectPhases', @level2type=N'COLUMN', @level2name=N'PhaseCode';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'階段名稱', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ProjectPhases', @level2type=N'COLUMN', @level2name=N'PhaseName';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'階段開始日', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ProjectPhases', @level2type=N'COLUMN', @level2name=N'StartDate';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'階段截止日', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ProjectPhases', @level2type=N'COLUMN', @level2name=N'EndDate';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'排序順序', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ProjectPhases', @level2type=N'COLUMN', @level2name=N'SortOrder';
GO

CREATE TABLE dbo.MT_ProjectTargets (
Id INT IDENTITY(1,1) PRIMARY KEY,
ProjectId INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Projects(Id),
QuestionTypeId INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_QuestionTypes(Id),
Level TINYINT,
TargetCount INT NOT NULL DEFAULT 0
);
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'各專案對各題型、等級的命題數量總體需求', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ProjectTargets';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'唯一識別碼', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ProjectTargets', @level2type=N'COLUMN', @level2name=N'Id';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'專案 ID', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ProjectTargets', @level2type=N'COLUMN', @level2name=N'ProjectId';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'題型 ID', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ProjectTargets', @level2type=N'COLUMN', @level2name=N'QuestionTypeId';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'等級 (配合前端若無區分則為NULL)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ProjectTargets', @level2type=N'COLUMN', @level2name=N'Level';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'目標命題數', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ProjectTargets', @level2type=N'COLUMN', @level2name=N'TargetCount';
GO

CREATE TABLE dbo.MT_ProjectMembers (
Id INT IDENTITY(1,1) PRIMARY KEY,
ProjectId INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Projects(Id),
UserId INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Users(Id),
JoinedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
CONSTRAINT UQ_MT_ProjectMembers_ProjectId_UserId UNIQUE (ProjectId, UserId)
);
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'關聯專案與參與的人員', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ProjectMembers';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'唯一識別碼', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ProjectMembers', @level2type=N'COLUMN', @level2name=N'Id';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'專案 ID', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ProjectMembers', @level2type=N'COLUMN', @level2name=N'ProjectId';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'使用者 ID', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ProjectMembers', @level2type=N'COLUMN', @level2name=N'UserId';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'加入專案時間', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ProjectMembers', @level2type=N'COLUMN', @level2name=N'JoinedAt';
GO

CREATE TABLE dbo.MT_ProjectMemberRoles (
Id INT IDENTITY(1,1) PRIMARY KEY,
ProjectMemberId INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_ProjectMembers(Id),
RoleCode TINYINT NOT NULL
);
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'支援一人在同一專案中具備多重任務身分', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ProjectMemberRoles';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'唯一識別碼', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ProjectMemberRoles', @level2type=N'COLUMN', @level2name=N'Id';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'成員 ID', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ProjectMemberRoles', @level2type=N'COLUMN', @level2name=N'ProjectMemberId';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'在該專案的角色任務 (對應 MT_Roles 的 Code)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ProjectMemberRoles', @level2type=N'COLUMN', @level2name=N'RoleCode';
GO

CREATE TABLE dbo.MT_MemberQuotas (
Id INT IDENTITY(1,1) PRIMARY KEY,
ProjectMemberId INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_ProjectMembers(Id),
QuestionTypeId INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_QuestionTypes(Id),
Level TINYINT,
QuotaCount INT NOT NULL DEFAULT 0
);
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'指派給特定人員的命題配額數量', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_MemberQuotas';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'唯一識別碼', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_MemberQuotas', @level2type=N'COLUMN', @level2name=N'Id';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'成員 ID', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_MemberQuotas', @level2type=N'COLUMN', @level2name=N'ProjectMemberId';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'題型 ID', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_MemberQuotas', @level2type=N'COLUMN', @level2name=N'QuestionTypeId';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'等級 (配合前端若無區分則為NULL)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_MemberQuotas', @level2type=N'COLUMN', @level2name=N'Level';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'指派數', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_MemberQuotas', @level2type=N'COLUMN', @level2name=N'QuotaCount';
GO

CREATE TABLE dbo.MT_Announcements (
Id INT IDENTITY(1,1) PRIMARY KEY,
Category TINYINT NOT NULL,
Status TINYINT NOT NULL DEFAULT 0,
ProjectId INT FOREIGN KEY REFERENCES dbo.MT_Projects(Id),
PublishDate DATE NOT NULL,
UnpublishDate DATE,
IsPinned BIT NOT NULL DEFAULT 0,
Title NVARCHAR(200) NOT NULL,
Content NVARCHAR(MAX) NOT NULL,
AuthorId INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Users(Id),
CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
UpdatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'系統公告與最新消息管理', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Announcements';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'唯一識別碼', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Announcements', @level2type=N'COLUMN', @level2name=N'Id';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'公告分類 (1:系統, 2:專案)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Announcements', @level2type=N'COLUMN', @level2name=N'Category';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'發佈狀態 (0:草稿, 1:發佈, 2:封存)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Announcements', @level2type=N'COLUMN', @level2name=N'Status';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'專案 ID (空為全域)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Announcements', @level2type=N'COLUMN', @level2name=N'ProjectId';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'發佈日期', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Announcements', @level2type=N'COLUMN', @level2name=N'PublishDate';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'截止日期', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Announcements', @level2type=N'COLUMN', @level2name=N'UnpublishDate';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否置頂', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Announcements', @level2type=N'COLUMN', @level2name=N'IsPinned';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'標題', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Announcements', @level2type=N'COLUMN', @level2name=N'Title';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'內容 (HTML)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Announcements', @level2type=N'COLUMN', @level2name=N'Content';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'發佈人 ID', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Announcements', @level2type=N'COLUMN', @level2name=N'AuthorId';
GO

---

## -- 5. 題目與命題群組 (核心業務表)

CREATE TABLE dbo.MT_Questions (
Id INT IDENTITY(1,1) PRIMARY KEY,
ProjectId INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Projects(Id),
QuestionTypeId INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_QuestionTypes(Id),
QuestionCode NVARCHAR(30) NOT NULL UNIQUE,
CreatorId INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Users(Id),
Status TINYINT NOT NULL DEFAULT 0,
Level TINYINT,
Difficulty TINYINT,
Stem NVARCHAR(MAX),
Analysis NVARCHAR(MAX),
CorrectAnswer NVARCHAR(10),
OptionA NVARCHAR(MAX),
OptionB NVARCHAR(MAX),
OptionC NVARCHAR(MAX),
OptionD NVARCHAR(MAX),
ArticleTitle NVARCHAR(200),
ArticleContent NVARCHAR(MAX),
AudioUrl NVARCHAR(500),
GradingNote NVARCHAR(MAX),
IsDeleted BIT NOT NULL DEFAULT 0,
DeletedAt DATETIME2,
CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
UpdatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'核心試題資料表，儲存所有類型的試題內容', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Questions';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'唯一識別碼', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Questions', @level2type=N'COLUMN', @level2name=N'Id';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'專案 ID', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Questions', @level2type=N'COLUMN', @level2name=N'ProjectId';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'題型 ID', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Questions', @level2type=N'COLUMN', @level2name=N'QuestionTypeId';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'試題系統編號', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Questions', @level2type=N'COLUMN', @level2name=N'QuestionCode';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'命題人 ID', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Questions', @level2type=N'COLUMN', @level2name=N'CreatorId';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'試題狀態數值 (0~13，共14種流轉狀態)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Questions', @level2type=N'COLUMN', @level2name=N'Status';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'等級 (0:初等/難度一, 1:中等/難度二, 2:中高等/難度三, 3:高等/難度四, 4:優等/難度五)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Questions', @level2type=N'COLUMN', @level2name=N'Level';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'難度感 (1:易, 2:中, 3:難)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Questions', @level2type=N'COLUMN', @level2name=N'Difficulty';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'題幹 (HTML)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Questions', @level2type=N'COLUMN', @level2name=N'Stem';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'試題解析 (HTML)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Questions', @level2type=N'COLUMN', @level2name=N'Analysis';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'正確答案', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Questions', @level2type=N'COLUMN', @level2name=N'CorrectAnswer';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'選項 A (HTML/圖)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Questions', @level2type=N'COLUMN', @level2name=N'OptionA';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'選項 B (HTML/圖)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Questions', @level2type=N'COLUMN', @level2name=N'OptionB';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'選項 C (HTML/圖)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Questions', @level2type=N'COLUMN', @level2name=N'OptionC';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'選項 D (HTML/圖)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Questions', @level2type=N'COLUMN', @level2name=N'OptionD';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'長文/題組標題', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Questions', @level2type=N'COLUMN', @level2name=N'ArticleTitle';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文章/語音內容 (HTML)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Questions', @level2type=N'COLUMN', @level2name=N'ArticleContent';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'音檔位址', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Questions', @level2type=N'COLUMN', @level2name=N'AudioUrl';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'批閱說明', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Questions', @level2type=N'COLUMN', @level2name=N'GradingNote';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否刪除', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Questions', @level2type=N'COLUMN', @level2name=N'IsDeleted';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'刪除時間', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Questions', @level2type=N'COLUMN', @level2name=N'DeletedAt';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'命題時間', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Questions', @level2type=N'COLUMN', @level2name=N'CreatedAt';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最後更新時間', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_Questions', @level2type=N'COLUMN', @level2name=N'UpdatedAt';
GO

CREATE TABLE dbo.MT_QuestionAttributes (
Id INT IDENTITY(1,1) PRIMARY KEY,
QuestionId INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Questions(Id),
AttributeKey TINYINT NOT NULL,
AttributeValue TINYINT NOT NULL
);
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'儲存試題的動態屬性（如主次類、文體、素材等）', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_QuestionAttributes';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'唯一識別碼', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_QuestionAttributes', @level2type=N'COLUMN', @level2name=N'Id';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'試題 ID', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_QuestionAttributes', @level2type=N'COLUMN', @level2name=N'QuestionId';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'屬性鍵 (0:主類, 1:次類, 2:文體, 3:素材來源, 4:寫作模式, 5:語音類型, 6:核心能力, 7:細目指標)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_QuestionAttributes', @level2type=N'COLUMN', @level2name=N'AttributeKey';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'屬性對應數值 (對應前端 Enum)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_QuestionAttributes', @level2type=N'COLUMN', @level2name=N'AttributeValue';
GO

CREATE TABLE dbo.MT_SubQuestions (
Id INT IDENTITY(1,1) PRIMARY KEY,
ParentQuestionId INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Questions(Id),
SortOrder INT NOT NULL DEFAULT 1,
Stem NVARCHAR(MAX),
CorrectAnswer NVARCHAR(10),
OptionA NVARCHAR(MAX),
OptionB NVARCHAR(MAX),
OptionC NVARCHAR(MAX),
OptionD NVARCHAR(MAX),
Analysis NVARCHAR(MAX),
CoreAbility NVARCHAR(100),
Indicator NVARCHAR(100),
FixedDifficulty TINYINT
);
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'題組型試題的子題目定義', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_SubQuestions';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'唯一識別碼', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_SubQuestions', @level2type=N'COLUMN', @level2name=N'Id';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'母題 ID', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_SubQuestions', @level2type=N'COLUMN', @level2name=N'ParentQuestionId';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'排序', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_SubQuestions', @level2type=N'COLUMN', @level2name=N'SortOrder';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'子題題幹', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_SubQuestions', @level2type=N'COLUMN', @level2name=N'Stem';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'子題正確答案', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_SubQuestions', @level2type=N'COLUMN', @level2name=N'CorrectAnswer';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'子題選項 A', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_SubQuestions', @level2type=N'COLUMN', @level2name=N'OptionA';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'子題選項 B', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_SubQuestions', @level2type=N'COLUMN', @level2name=N'OptionB';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'子題選項 C', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_SubQuestions', @level2type=N'COLUMN', @level2name=N'OptionC';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'子題選項 D', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_SubQuestions', @level2type=N'COLUMN', @level2name=N'OptionD';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'子題解析', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_SubQuestions', @level2type=N'COLUMN', @level2name=N'Analysis';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'核心能力', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_SubQuestions', @level2type=N'COLUMN', @level2name=N'CoreAbility';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'細目指標', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_SubQuestions', @level2type=N'COLUMN', @level2name=N'Indicator';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'固定難度 (1:易, 2:中, 3:難)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_SubQuestions', @level2type=N'COLUMN', @level2name=N'FixedDifficulty';
GO

CREATE TABLE dbo.MT_QuestionHistoryLogs (
Id INT IDENTITY(1,1) PRIMARY KEY,
QuestionId INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Questions(Id),
UserId INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Users(Id),
Action TINYINT NOT NULL,
Comment NVARCHAR(MAX),
OldStatus TINYINT,
NewStatus TINYINT,
CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'記錄試題的完整流轉歷程與各方審核意見', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_QuestionHistoryLogs';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'唯一識別碼', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_QuestionHistoryLogs', @level2type=N'COLUMN', @level2name=N'Id';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'試題 ID', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_QuestionHistoryLogs', @level2type=N'COLUMN', @level2name=N'QuestionId';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'操作人 ID', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_QuestionHistoryLogs', @level2type=N'COLUMN', @level2name=N'UserId';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'操作動作數值 (1:建立, 2:編輯, 3:送審, 4:退回...)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_QuestionHistoryLogs', @level2type=N'COLUMN', @level2name=N'Action';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'審查意見/備註', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_QuestionHistoryLogs', @level2type=N'COLUMN', @level2name=N'Comment';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'原狀態碼數值', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_QuestionHistoryLogs', @level2type=N'COLUMN', @level2name=N'OldStatus';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'新狀態碼數值', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_QuestionHistoryLogs', @level2type=N'COLUMN', @level2name=N'NewStatus';
GO

CREATE TABLE dbo.MT_RevisionReplies (
Id INT IDENTITY(1,1) PRIMARY KEY,
QuestionId INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Questions(Id),
UserId INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Users(Id),
Stage TINYINT NOT NULL,
Content NVARCHAR(MAX) NOT NULL,
CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'命題教師對審核意見的回覆與修改說明', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_RevisionReplies';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'唯一識別碼', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_RevisionReplies', @level2type=N'COLUMN', @level2name=N'Id';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'試題 ID', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_RevisionReplies', @level2type=N'COLUMN', @level2name=N'QuestionId';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'回覆人 ID', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_RevisionReplies', @level2type=N'COLUMN', @level2name=N'UserId';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'修題階段 (1:一審修題, 2:二審修題...)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_RevisionReplies', @level2type=N'COLUMN', @level2name=N'Stage';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'修題說明', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_RevisionReplies', @level2type=N'COLUMN', @level2name=N'Content';
GO

CREATE TABLE dbo.MT_ReviewAssignments (
Id INT IDENTITY(1,1) PRIMARY KEY,
QuestionId INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Questions(Id),
ProjectId INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Projects(Id),
ReviewerId INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Users(Id),
ReviewStage TINYINT NOT NULL,
ReviewStatus TINYINT NOT NULL DEFAULT 0,
Decision TINYINT,
Comment NVARCHAR(MAX),
DecidedAt DATETIME2,
CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
CONSTRAINT UQ_MT_ReviewAssignments_Question_Reviewer_Stage UNIQUE (QuestionId, ReviewerId, ReviewStage)
);
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'管理命題中的三審指派任務', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ReviewAssignments';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'唯一識別碼', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ReviewAssignments', @level2type=N'COLUMN', @level2name=N'Id';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'試題 ID', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ReviewAssignments', @level2type=N'COLUMN', @level2name=N'QuestionId';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'專案 ID', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ReviewAssignments', @level2type=N'COLUMN', @level2name=N'ProjectId';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'審題人 ID', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ReviewAssignments', @level2type=N'COLUMN', @level2name=N'ReviewerId';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'審題階段 (1:一審, 2:二審, 3:總召)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ReviewAssignments', @level2type=N'COLUMN', @level2name=N'ReviewStage';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'任務狀態 (0:待審, 1:審核中, 2:已完成)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ReviewAssignments', @level2type=N'COLUMN', @level2name=N'ReviewStatus';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最終決策 (1:通過, 2:修正, 3:退回)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ReviewAssignments', @level2type=N'COLUMN', @level2name=N'Decision';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'審查意見', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ReviewAssignments', @level2type=N'COLUMN', @level2name=N'Comment';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'決策時間', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ReviewAssignments', @level2type=N'COLUMN', @level2name=N'DecidedAt';
GO

CREATE TABLE dbo.MT_ReviewReturnCounts (
Id INT IDENTITY(1,1) PRIMARY KEY,
QuestionId INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Questions(Id),
FinalReviewerId INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Users(Id),
ReturnCount INT NOT NULL DEFAULT 0,
CanEditByReviewer BIT NOT NULL DEFAULT 0
);
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'追蹤試題在總召階段的退回次數，強制截止限制', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ReviewReturnCounts';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'唯一識別碼', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ReviewReturnCounts', @level2type=N'COLUMN', @level2name=N'Id';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'試題 ID', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ReviewReturnCounts', @level2type=N'COLUMN', @level2name=N'QuestionId';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'總召 ID', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ReviewReturnCounts', @level2type=N'COLUMN', @level2name=N'FinalReviewerId';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已退回次數', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ReviewReturnCounts', @level2type=N'COLUMN', @level2name=N'ReturnCount';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'解鎖總召自行修題', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_ReviewReturnCounts', @level2type=N'COLUMN', @level2name=N'CanEditByReviewer';
GO

CREATE TABLE dbo.MT_SimilarityChecks (
Id INT IDENTITY(1,1) PRIMARY KEY,
SourceQuestionId INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Questions(Id),
ComparedQuestionId INT NOT NULL FOREIGN KEY REFERENCES dbo.MT_Questions(Id),
SimilarityScore DECIMAL(5,2) NOT NULL,
Determination TINYINT NOT NULL,
CheckedBy INT FOREIGN KEY REFERENCES dbo.MT_Users(Id),
CheckedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'儲存 AI 或人工執行的試題相似度查重報告', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_SimilarityChecks';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'來源題目 ID', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_SimilarityChecks', @level2type=N'COLUMN', @level2name=N'SourceQuestionId';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'比對目標 ID', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_SimilarityChecks', @level2type=N'COLUMN', @level2name=N'ComparedQuestionId';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'相似度分數', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_SimilarityChecks', @level2type=N'COLUMN', @level2name=N'SimilarityScore';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'查重結果判定 (1:安全, 2:相似度高, 3:確認重複)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'MT_SimilarityChecks', @level2type=N'COLUMN', @level2name=N'Determination';
GO

---

## -- 6. 建立非唯一索引 (Non-Unique Indexes)

CREATE INDEX IX_MT_Users_RoleId ON dbo.MT_Users(RoleId);
CREATE INDEX IX_MT_Users_Status ON dbo.MT_Users(Status);
CREATE INDEX IX_MT_Users_Email ON dbo.MT_Users(Email);

CREATE INDEX IX_MT_Questions_ProjectId ON dbo.MT_Questions(ProjectId);
CREATE INDEX IX_MT_Questions_Status ON dbo.MT_Questions(Status);
CREATE INDEX IX_MT_Questions_CreatorId ON dbo.MT_Questions(CreatorId);
GO
