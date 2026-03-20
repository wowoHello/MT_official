# PRD: 登入頁面（Login Page）

> **文件版本**：v1.0
> **最後更新**：2026-03-19
> **關聯檔案**：`index.html`、`js/login.js`、`js/shared.js`
> **目標遷移框架**：Blazor .NET 10

---

## 1. 頁面概述

登入頁面為 CWT 命題工作平臺的入口，所有使用者（例如：管理員、命題教師、審題教師）皆須通過此頁面驗證身份後方可進入系統。頁面提供帳號密碼驗證、圖形驗證碼、記住登入狀態、忘記密碼重設等功能。

---

## 2. 使用者角色與登入後導向

### 2.1 角色定義

| 角色 | 帳號建立方式 | 登入後可見功能卡片 |
|------|-------------|-------------------|
| **系統管理員（ADMIN）** | 系統初始給予一組帳密 | 命題儀表板、命題專案管理、命題總覽、教師管理系統、角色與權限管理、系統公告/使用說明（可讀可寫） |
| **命題教師（TEACHER）** | 由「角色與權限管理」頁面建立 | 命題任務（命題與修題時可編輯）、審題任務（僅互審可編輯）、系統公告/使用說明（唯讀） |
| **審題教師（REVIEWER）** | 由「角色與權限管理」頁面建立 | 審題任務（僅被分配的審題區間可編輯）、系統公告/使用說明（唯讀） |

### 2.2 登入後行為

- 登入成功後跳轉至首頁（`firstpage.html`）
- 首頁根據**頂部導覽列的梯次選擇**（預設最新梯次，或上一回離開前觀看的梯次）與**使用者角色權限**動態顯示對應的功能卡片
- 梯次記憶機制：使用者上次離開時觀看的梯次會被記錄，下次登入時自動還原

---

## 3. 功能需求

### 3.1 帳號密碼輸入

| 欄位 | 說明 |
|------|------|
| 帳號 | 文字輸入框，必填，placeholder「請輸入帳號」 |
| 密碼 | 密碼輸入框，必填，placeholder「請輸入密碼」，附顯示/隱藏切換按鈕（眼睛圖示） |

**密碼顯示/隱藏切換邏輯：**
- 預設為隱藏（`type="password"`）
- 點擊眼睛圖示切換為顯示（`type="text"`），圖示同步切換（`fa-eye-slash` ↔ `fa-eye`）

### 3.2 記住登入狀態（90 天）

- Checkbox 勾選項，預設未勾選
- 勾選後登入成功時，將帳號與當前時間戳記存入 localStorage
- 下次進入登入頁面時，自動檢查：
  - 若記錄存在且未超過 90 天 → 自動帶入帳號欄位並勾選 checkbox
  - 若記錄已超過 90 天 → 清除記錄，不自動帶入
- 90 天的設計考量：約為一個產學合作命題專案的完整週期長度

**localStorage 鍵值：**

| Key | Value | 說明 |
|-----|-------|------|
| `cwt_remembered_account` | 帳號字串 | 記住的帳號 |
| `cwt_remembered_time` | 時間戳（毫秒） | 記住時的時間點 |

### 3.3 圖形驗證碼

- 使用 Canvas 繪製 6 碼隨機英數混合驗證碼
- 字元集排除易混淆字元（`I`、`l`、`1`、`O`、`0`）
- 驗證碼**不分大小寫**比對
- Canvas 繪製內容包含：
  - 背景色（淺灰 `#f8fafc`）
  - 6 條隨機顏色干擾線
  - 40 個隨機噪點
  - 文字隨機偏移與旋轉（增加辨識難度）
- 重新產生方式：
  - 點擊 Canvas 本身
  - 點擊旁邊的重新整理按鈕（`fa-rotate-right`）
- 驗證碼錯誤時：清空輸入、重新產生驗證碼、自動 focus 至驗證碼欄位

**Blazor 遷移備註：**
> Canvas 繪圖邏輯在 Blazor 伺服器端渲染時較難以原生 C# 執行。遷移時建議：
> 1. 保留 JavaScript Interop 方式呼叫 JS 函式進行驗證碼重繪，或
> 2. 改由後端產出 Base64 圖片回傳給 `<img>` 標籤（建議方案，可同時實現伺服器端驗證）

### 3.4 資安提示宣讀

- 位於驗證碼下方、登入按鈕上方
- 靜態顯示，藍色提示框樣式
- 內容：
  1. 請勿將帳號密碼交給他人使用。
  2. 登入後若需離開座位，請確實登出系統。
  3. 建議定期更換您的密碼以確保帳號安全。

### 3.5 登入系統按鈕

- 點擊後執行登入流程
- 按鈕狀態切換：
  - **一般狀態**：顯示「登入系統」文字 + 箭頭圖示
  - **載入中狀態**：顯示「登入中...」+ Spinner 旋轉動畫，同時禁用所有輸入欄位與按鈕，防止重複提交
- 登入成功：顯示 SweetAlert2 成功提示（1 秒後自動關閉），跳轉至 `firstpage.html`
- 登入失敗：頂部顯示紅色錯誤提示列，表單輕微抖動動畫提醒

**前端驗證規則：**

| 驗證項目 | 錯誤訊息 |
|---------|---------|
| 帳號或密碼為空 | 「請輸入完整的帳號密碼。」 |
| 驗證碼錯誤 | 「驗證碼錯誤，請重新輸入。」 |
| 系統例外 | 「系統登入發生例外錯誤，請稍後再試。」 |

**Blazor 遷移備註：**
> 遷移時需將前端模擬的登入邏輯替換為實際的 C# HttpClient 登入 API 呼叫，並透過 `AuthenticationStateProvider` 處理 JWT / Session 使用者憑證狀態。前端防呆邏輯可維持，但核心登入驗證必須交由後端處理。

### 3.6 忘記密碼功能

**流程（兩步驟）：**

```
步驟一：輸入信箱 → 發送含 GUID 重設連結至信箱
          ↓
步驟二：點擊信件連結 → 導至密碼重設畫面 → 設定新密碼
```

**步驟一：發送重設連結**
- 點擊「忘記密碼？」開啟 Modal 彈窗
- 輸入註冊的電子信箱
- 點擊「發送重設連結」→ 模擬發送延遲 → 顯示成功提示
- 信箱為空時顯示 SweetAlert2 錯誤提示

**步驟二：重設密碼**
- 顯示欄位：舊密碼（唯讀展示，Demo 用途）、新密碼、確認新密碼
- 防呆驗證規則：

| 驗證項目 | 處理方式 |
|---------|---------|
| 新密碼或確認密碼為空 | SweetAlert2 警示「請完整填寫新密碼與確認密碼。」 |
| 兩次新密碼不一致 | SweetAlert2 警示「兩次輸入的新密碼不同！」 |
| 新密碼與舊密碼相同 | 紅字顯示「新密碼不可與舊密碼相同」，阻止送出 |

- 重設成功後：SweetAlert2 成功提示 → 自動關閉 Modal → 返回登入頁面重新登入

**Blazor 遷移備註：**
> 實際實作時，重設連結應包含伺服器產生的 GUID Token（建議有效期 10 分鐘），使用者點擊連結後由後端驗證 Token 有效性。信件發送需整合 SMTP 服務（如 SendGrid、AWS SES 等）。

---

## 4. 共用元件

### 4.1 文字大小調整工具球

- 固定浮動於畫面右下角（所有頁面共用，由 `shared.js` 注入）
- Speed Dial 設計：hover 時向上展開功能按鈕群
- 功能按鈕：
  - **放大字體**（+A）：每次增加 5%
  - **預設大小**（100%）：重置為 100%
  - **縮小字體**（-A）：每次減少 5%
  - **留言通知**（未來擴充項目，目前為不可點擊的灰色狀態）
- 字體縮放範圍：90% ~ 130%
- 主按鈕支援拖拽移動，位置記憶於 localStorage
- 視窗大小改變時自動修正位置防止超出畫面

**localStorage 鍵值：**

| Key | Value | 說明 |
|-----|-------|------|
| `cwt_font_scale` | 數字（90~130） | 當前字體縮放百分比 |
| `cwt_font_pos` | `{ left, top }` 或 `{ bottom, right }` | 工具球位置 |

### 4.2 留言通知（未來更新項目）

- 目前僅顯示為灰色不可點擊狀態
- Tooltip 提示「留言通知（準備中）」
- 規劃為後續版本擴充功能

---

## 5. 頁面視覺規格

### 5.1 版面佈局

- 左側 60%：背景圖片（`login-bg.png`），附漸層遮罩過渡至右側
- 右側 40%：毛玻璃登入卡片（`glass-card`），垂直置中
- 行動裝置：背景圖隱藏，登入卡片置中全寬

### 5.2 色彩配置

| 用途 | 色碼 | 名稱 |
|------|------|------|
| 頁面底色 | `#FBF9F6` | Oat Milk White |
| 主要按鈕/品牌色 | `#6B8EAD` | Morandi Blue-Gray |
| 成功/正向操作 | `#8EAB94` | Sage Green |
| 警告/錯誤 | `#D98A6C` | Warm Terracotta |
| 主要文字 | `#374151` | Slate Main |

### 5.3 字型

- 主字型：Noto Sans TC（Google Fonts）
- 粗細：400（一般）、500（中等）、700（粗體）

---

## 6. 登入後使用者資料結構

登入成功後存入 localStorage 的使用者物件：

```json
{
  "id": "T1001",
  "name": "系統管理員",
  "role": "ADMIN",
  "loginTime": "2026-03-19T10:30:00.000Z"
}
```

**localStorage 鍵值：**

| Key | Value | 說明 |
|-----|-------|------|
| `cwt_user` | JSON 物件 | 當前登入使用者資訊 |
| `cwt_current_project` | 專案 ID 字串（如 `P2026-01`） | 當前選中的梯次，登出時保留以供下次登入還原 |

---

## 7. 使用情境

### 情境一：系統管理員登入

1. 管理員進入登入頁面
2. 輸入系統初始給予的帳號與密碼
3. 勾選「記住登入狀態（90 天）」
4. 輸入 6 碼圖形驗證碼
5. 查看資安提示
6. 點擊【登入系統】
7. 登入成功，跳轉至首頁
8. 首頁根據導覽列梯次（預設最新梯次或上次離開時的梯次）顯示功能卡片：
   - 命題儀表板
   - 命題專案管理
   - 命題總覽
   - 教師管理系統
   - 角色與權限管理
   - 系統公告/使用說明（可讀可寫）

### 情境二：命題教師登入

1. 命題教師進入登入頁面
2. 輸入帳號與密碼（帳號由「角色與權限管理」建立）
3. 勾選「記住登入狀態（90 天）」
4. 輸入 6 碼圖形驗證碼
5. 查看資安提示
6. 點擊【登入系統】
7. 登入成功，跳轉至首頁
8. 首頁根據導覽列梯次與權限顯示功能卡片：
   - 命題任務（命題與修題時可編輯）
   - 審題任務（僅互審可編輯）
   - 系統公告/使用說明（唯讀）

### 情境三：審題教師登入

1. 審題教師進入登入頁面
2. 輸入帳號與密碼（帳號由「角色與權限管理」建立）
3. 勾選「記住登入狀態（90 天）」
4. 輸入 6 碼圖形驗證碼
5. 查看資安提示
6. 點擊【登入系統】
7. 登入成功，跳轉至首頁
8. 首頁根據導覽列梯次與權限顯示功能卡片：
   - 審題任務（僅被分配的審題區間可編輯）
   - 系統公告/使用說明（唯讀）

---

## 8. 資料庫資料表規劃

### 8.1 Users（使用者帳號表）

| 欄位名稱 | 資料型態 | 約束 | 說明 |
|----------|---------|------|------|
| Id | UNIQUEIDENTIFIER | PK, DEFAULT NEWID() | 使用者唯一識別碼 |
| Account | NVARCHAR(50) | NOT NULL, UNIQUE | 登入帳號 |
| PasswordHash | NVARCHAR(256) | NOT NULL | 密碼雜湊值（建議 bcrypt/Argon2） |
| Name | NVARCHAR(50) | NOT NULL | 使用者顯示名稱 |
| Email | NVARCHAR(100) | NULL | 電子信箱（忘記密碼用） |
| RoleId | UNIQUEIDENTIFIER | FK → Roles.Id, NOT NULL | 角色外鍵 |
| IsActive | BIT | DEFAULT 1 | 帳號是否啟用 |
| IsFirstLogin | BIT | DEFAULT 1 | 是否為首次登入（首次登入強制改密碼） |
| RememberToken | NVARCHAR(256) | NULL | 記住登入狀態的 Token |
| RememberExpiry | DATETIME2 | NULL | 記住登入 Token 到期時間 |
| LastLoginAt | DATETIME2 | NULL | 最後登入時間 |
| LastProjectId | UNIQUEIDENTIFIER | FK → Projects.Id, NULL | 上次離開時的梯次 ID |
| CreatedAt | DATETIME2 | DEFAULT GETUTCDATE() | 建立時間 |
| UpdatedAt | DATETIME2 | DEFAULT GETUTCDATE() | 最後更新時間 |

### 8.2 Roles（角色表）

| 欄位名稱 | 資料型態 | 約束 | 說明 |
|----------|---------|------|------|
| Id | UNIQUEIDENTIFIER | PK, DEFAULT NEWID() | 角色唯一識別碼 |
| Name | NVARCHAR(50) | NOT NULL, UNIQUE | 角色名稱（如：系統管理員、命題教師、審題教師） |
| Code | NVARCHAR(20) | NOT NULL, UNIQUE | 角色代碼（如：ADMIN、TEACHER、REVIEWER） |
| IsSystem | BIT | DEFAULT 0 | 是否為系統預設角色（不可刪除） |
| Description | NVARCHAR(200) | NULL | 角色描述 |
| CreatedAt | DATETIME2 | DEFAULT GETUTCDATE() | 建立時間 |

### 8.3 RolePermissions（角色權限表）

| 欄位名稱 | 資料型態 | 約束 | 說明 |
|----------|---------|------|------|
| Id | UNIQUEIDENTIFIER | PK, DEFAULT NEWID() | 唯一識別碼 |
| RoleId | UNIQUEIDENTIFIER | FK → Roles.Id, NOT NULL | 角色外鍵 |
| PermissionCode | NVARCHAR(50) | NOT NULL | 權限代碼（如：dashboard.view、project.manage） |
| IsEnabled | BIT | DEFAULT 1 | 是否啟用 |

**權限代碼對照（與登入後功能卡片顯示相關）：**

| 權限代碼 | 對應功能 | ADMIN | TEACHER | REVIEWER |
|---------|---------|-------|---------|----------|
| `dashboard.view` | 命題儀表板 | ✓ | ✗ | ✗ |
| `project.manage` | 命題專案管理 | ✓ | ✗ | ✗ |
| `overview.view` | 命題總覽 | ✓ | ✗ | ✗ |
| `proposition.edit` | 命題任務 | ✓ | ✓ | ✗ |
| `review.edit` | 審題任務 | ✓ | ✓（僅互審） | ✓（僅分配區間） |
| `teacher.manage` | 教師管理系統 | ✓ | ✗ | ✗ |
| `role.manage` | 角色與權限管理 | ✓ | ✗ | ✗ |
| `announcement.write` | 系統公告（可寫） | ✓ | ✗ | ✗ |
| `announcement.read` | 系統公告（唯讀） | ✓ | ✓ | ✓ |

### 8.4 PasswordResetTokens（密碼重設 Token 表）

| 欄位名稱 | 資料型態 | 約束 | 說明 |
|----------|---------|------|------|
| Id | UNIQUEIDENTIFIER | PK, DEFAULT NEWID() | 唯一識別碼 |
| UserId | UNIQUEIDENTIFIER | FK → Users.Id, NOT NULL | 使用者外鍵 |
| Token | NVARCHAR(128) | NOT NULL, UNIQUE | GUID Token（信件連結中的識別碼） |
| ExpiresAt | DATETIME2 | NOT NULL | Token 到期時間（建議 10 分鐘有效） |
| IsUsed | BIT | DEFAULT 0 | 是否已使用 |
| CreatedAt | DATETIME2 | DEFAULT GETUTCDATE() | 建立時間 |

### 8.5 LoginLogs（登入日誌表）

| 欄位名稱 | 資料型態 | 約束 | 說明 |
|----------|---------|------|------|
| Id | UNIQUEIDENTIFIER | PK, DEFAULT NEWID() | 唯一識別碼 |
| UserId | UNIQUEIDENTIFIER | FK → Users.Id, NOT NULL | 使用者外鍵 |
| LoginAt | DATETIME2 | DEFAULT GETUTCDATE() | 登入時間 |
| IpAddress | NVARCHAR(45) | NULL | 登入 IP 位址 |
| UserAgent | NVARCHAR(500) | NULL | 瀏覽器 UserAgent |
| IsSuccess | BIT | NOT NULL | 是否登入成功 |
| FailReason | NVARCHAR(100) | NULL | 失敗原因（如：密碼錯誤、驗證碼錯誤） |

---

## 9. API 端點規劃（Blazor .NET 10 遷移參考）

| 方法 | 路徑 | 說明 |
|------|------|------|
| POST | `/api/auth/login` | 帳號密碼登入，回傳 JWT Token |
| POST | `/api/auth/logout` | 登出，清除 Session/Token |
| POST | `/api/auth/forgot-password` | 發送密碼重設信件（含 GUID Token） |
| POST | `/api/auth/reset-password` | 驗證 Token 並重設密碼 |
| GET | `/api/auth/captcha` | 取得伺服器端產生的驗證碼圖片（Base64） |
| POST | `/api/auth/verify-captcha` | 驗證驗證碼（伺服器端比對） |
| GET | `/api/auth/me` | 取得當前登入使用者資訊與權限 |

---

## 10. 已知限制與改進方向

| 項目 | 現況（Demo） | 遷移後目標 |
|------|-------------|-----------|
| 登入驗證 | 前端模擬，任何帳密皆可登入 | 後端 API 驗證 + JWT/Cookie 認證 |
| 密碼儲存 | 無密碼儲存邏輯 | bcrypt/Argon2 雜湊加鹽 |
| 驗證碼 | Canvas 前端繪製與比對 | 伺服器端產生 + Session 比對 |
| 記住登入 | localStorage 存帳號 + 時間戳 | HttpOnly Cookie + Refresh Token |
| 忘記密碼 | 純前端 Modal 模擬 | 真實 SMTP 寄信 + GUID Token 驗證 |
| 角色判斷 | 僅前端 `role` 欄位判斷 | 後端 RBAC + 權限矩陣查詢 |
| 登入日誌 | 無 | LoginLogs 資料表記錄 |
