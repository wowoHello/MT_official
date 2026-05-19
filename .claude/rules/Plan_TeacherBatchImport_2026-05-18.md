# 教師批次匯入功能計畫書

> **計畫日期**：2026-05-18
> **作者**：Claude（Teachers 頁面維護架構師）
> **狀態**：✅ 決策定稿 — 等待使用者授權實作
> **對應按鈕**：Teachers.razor 右上角「批次匯入」（現況按鈕已存在，尚無功能）

---

## 一、Excel 欄位對照表

公版檔案：`D:\jay_liu\Desktop\教師資料匯入_公版.xlsx`
工作表：`教師資料匯入區`
讀取範圍：第 2 列起，B 欄 ~ N 欄（A 欄序號省略，第 1 列標題省略）

| 欄 | Excel 標題 | Model 屬性 | DB 欄位 | 資料表 | 型別 | 長度 | 必填 | 備註 |
|---|---|---|---|---|---|---|---|---|
| B | 姓名* | `DisplayName` | `DisplayName` | MT_Users | NVARCHAR | 50 | 是 | |
| C | 性別 | `Gender` | `Gender` | MT_Teachers | TINYINT | — | 否 | 0=未知, 1=男, 2=女；Excel 填「男」/「女」/空白，程式轉數字 |
| D | 電子信箱(帳號)* | `Email` | `Email` + `Username` | MT_Users | NVARCHAR | 200 | 是 | 同時作為登入 Username |
| E | 聯絡電話 | `Phone` | `Phone` | MT_Teachers | NVARCHAR | 20 | 否 | |
| F | 身分證字號 | `IdNumber` | `IdNumber` | MT_Teachers | NVARCHAR | 200 | 否 | 系統以明文儲存（現況），前端遮罩顯示 |
| G | 任教學校* | `School` | `School` | MT_Teachers | NVARCHAR | 100 | 是 | |
| H | 系所/科別 | `Department` | `Department` | MT_Teachers | NVARCHAR | 50 | 否 | |
| I | 職稱* | `Title` | `Title` | MT_Teachers | NVARCHAR | 20 | 是 | 將出現在正式聘書 |
| J | 專長領域 | `Expertise` | `Expertise` | MT_Teachers | NVARCHAR | 200 | 否 | |
| K | 教學年資 | `TeachingYears` | `TeachingYears` | MT_Teachers | INT | — | 否 | 正整數 |
| L | 最高學歷 | `Education` | `Education` | MT_Teachers | TINYINT | — | 否 | 1=學士, 2=碩士, 3=博士；Excel 填中文名，程式轉數字 |
| M | 帳號狀態* | `Status` | `Status` | MT_Users | TINYINT | — | 是 | 1=啟用, 0=停用；Excel 填「啟用」/「停用」 |
| N | 備註 | `Note` | `Note` | MT_Teachers | NVARCHAR | 500 | 否 | |

**自動帶入欄位（不在 Excel 中）**：
- `Username`：與 Email 相同
- `PasswordHash`：PBKDF2(`CSF@01024304`)，呼叫現有 `AuthService.HashPassword`
- `IsFirstLogin`：固定 `1`
- `RoleId`：查詢 `MT_Roles WHERE Name = N'預設教師'`（與現有 `CreateTeacherAsync` 同邏輯）
- `TeacherCode`：依現有規則自動產生（T + 民國年 + 3碼流水號）

---

## 二、資料驗證規則

### 必填檢查
| 欄位 | 規則 | 錯誤訊息 |
|---|---|---|
| 姓名（B） | 不可空白 | 「姓名為必填欄位」 |
| 電子信箱（D） | 不可空白 + email 格式 | 「信箱格式不正確」 |
| 任教學校（G） | 不可空白 | 「任教學校為必填欄位」 |
| 職稱（I） | 不可空白 | 「職稱為必填欄位」 |
| 帳號狀態（M） | 必須為「啟用」或「停用」 | 「帳號狀態必須填寫「啟用」或「停用」」 |

### 格式驗證
| 欄位 | 規則 |
|---|---|
| Email | Regex `^[^@\s]+@[^@\s]+\.[^@\s]+$`；長度 ≤ 200 |
| 教學年資 | 純數字，0~99；非數字字串 → 錯誤；空白 → 允許（null） |
| 性別 | 「男」→ 1，「女」→ 2，空白 → 0（未知）；其他值 → 警告但不擋 |
| 最高學歷 | 「學士」→ 1，「碩士」→ 2，「博士」→ 3，空白 → null；其他值 → 警告 |
| 姓名長度 | ≤ 50 字元 |
| 職稱長度 | ≤ 20 字元（影響正式聘書） |
| 備註長度 | ≤ 500 字元 |

### Email 重複檢查（兩層）— ✅ Q1 決策已確認

1. **檔內重複**：解析完成後，在 C# 端以 `HashSet<string>` 偵測同一檔案內重複 Email。
   - 重複的第二筆及以後：標為**紅色錯誤列**（`RowStatus = Error`），checkbox 禁用，強制排除，不可匯入。
   - 錯誤訊息：「與檔案第 N 列 Email 重複」

2. **DB 重複**：`Step B 預覽` 階段，對所有格式驗證通過列的 Email 批次查詢 `SELECT Email FROM MT_Users WHERE Email IN (@emails)`，標記已存在者。
   - **✅ 已確認**：標為**橘色警告列**（`RowStatus = Warning`），預設勾選（`IsSelected = true`），使用者可手動取消勾選以跳過。
   - 若使用者保留勾選 → `ImportTeachersAsync` 寫入時若觸發 `UNIQUE` 撞擊（SqlException 2601/2627）→ 記錄「Email 信箱已存在」到失敗清單，該列計入失敗計數。
   - 警告訊息：「此 Email 已存在於系統，若保留勾選匯入可能因 UNIQUE 約束失敗」

---

## 三、UI 流程設計

### 入口
點擊 Teachers.razor 右上角「批次匯入」按鈕 → 開啟全寬 Slide-over 面板（`ImportSlideOver`，寬度 `w-full max-w-4xl`）。

### Step A：上傳區
```
┌─────────────────────────────────────────────────────────┐
│  拖曳 Excel 檔案至此，或點擊選擇檔案                      │
│  僅支援 .xlsx 格式，最大 5MB                              │
│                                                          │
│  [下載公版範本]                               [選擇檔案] │
└─────────────────────────────────────────────────────────┘
```
- 接受 `.xlsx` 檔案（MIME: `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`）
- 不接受 `.xls`（NPOI HSSFWorkbook 需另外處理，範圍外）
- 檔案大小上限：5MB（與現有圖片上傳端點一致）
- 「下載公版範本」：提供 `教師資料匯入_公版.xlsx` 的下載連結（放置路徑：`wwwroot/temp/`）
- 選擇檔案後立即呼叫 `ParseExcelAsync`，顯示 Loading spinner

### Step B：預覽與驗證結果

解析完成後顯示預覽表格（使用 Tailwind `overflow-x-auto` 橫向捲動）：

| # | 姓名 | 性別 | 電子信箱 | 學校 | 職稱 | 帳號狀態 | 驗證結果 | 勾選 |
|---|---|---|---|---|---|---|---|---|
| 2 | 王小明 | 男 | w@test.com | 台大 | 教授 | 啟用 | 綠色「OK」 | [v 可勾] |
| 3 | （空白） | — | bad-email | ... | ... | 啟用 | 紅字「姓名為必填欄位」 | [x 不可勾] |
| 4 | 李大華 | — | dup@db.com | ... | ... | 啟用 | 橘字「Email 已存在於系統，保留勾選仍可能失敗」 | [v 預設勾，可取消] |
| 5 | 陳阿花 | 女 | dup@file.com | ... | ... | 啟用 | 紅字「與檔案第 6 列 Email 重複」 | [x 不可勾] |

**三種列顏色語意（最終確認）**：

| 顏色 | RowStatus | 原因 | checkbox | 預設狀態 |
|---|---|---|---|---|
| 紅色 | `Error` | 必填空白 / 格式錯誤 / 檔內 Email 重複 | 禁用（顯示叉叉圖示） | 強制排除 |
| 橘色 | `Warning` | DB 中 Email 已存在 | 啟用 | **預設勾選**（使用者可手動取消） |
| 綠色（預設） | `Valid` | 全部驗證通過 | 啟用 | 預設勾選 |

**統計列**：
```
共 25 列 · 可匯入 22 筆 · 錯誤 2 筆（不可匯入）· 警告 1 筆（DB Email 已存在）
```

底部按鈕：`[取消]` `[確認匯入 N 筆]`（N = 目前勾選中的有效列數，即時更新）

### Step C：執行匯入與結果
點擊確認 → 顯示進度提示 `正在匯入...`（禁用按鈕防重複提交）→ 完成後顯示結果摘要：
```
匯入完成
成功：22 筆
失敗：0 筆
```
若有部份失敗（DB 寫入途中發生），顯示失敗列的 RowNumber + 錯誤訊息列表，方便使用者對照 Excel 修正再重傳。
按「完成」關閉 Slide-over，觸發 `LoadTeachersAsync()` 重新載入教師列表。

---

## 四、Service 方法簽章

### `ParseExcelAsync`

```csharp
// TeacherService.cs 新增
// 解析失敗（如工作表不存在）以 tuple 第二欄位回傳錯誤訊息
Task<(List<BatchImportRow> Rows, string? ParseError)> ParseExcelAsync(Stream fileStream);
```

**邏輯**：
1. 用 NPOI `XSSFWorkbook` 讀取 `.xlsx`（已有 NuGet 宣告）
2. 找工作表 `教師資料匯入區`（找不到 → 回傳 `(new List<BatchImportRow>(), "Excel 格式不符，請下載公版範本重新填寫")`）
3. 跳過第 1 列（標題）
4. 逐列讀 B~N 欄（B=1, N=13；NPOI 0-based 故為 col 1~13）
5. 若整列 B~N 全空白 → 停止讀取（視為資料結束）
6. 每列建立 `BatchImportRow`，執行格式驗證（資料填入 `row.Data`，即 `CreateTeacherRequest`）
7. 用 `HashSet<string>` 偵測檔內 Email 重複（第二筆標 Error）
8. 批次查詢 DB 重複 Email（`WHERE Email IN (...)`），標 Warning
9. 回傳 `(rows, null)`

### `ImportTeachersAsync`

```csharp
// TeacherService.cs 新增
// 直接回 List；UI 端用 LINQ 算 SuccessCount / FailureCount
Task<List<BatchImportRowResult>> ImportTeachersAsync(
    IReadOnlyList<BatchImportRow> rows,
    int operatorId);
```

**邏輯（✅ Q2 已確認：逐筆記錄，不整批回滾）**：

1. 過濾 `IsSelected == true` 且 `Status != Error` 的列（含 Valid 與 Warning 列）
2. 宣告 `var results = new List<BatchImportRowResult>()`
3. **不開跨批次 Transaction**；每筆教師各自獨立 try/catch：
   ```
   foreach 每筆 row：
     try
     {
       using var tx = conn.BeginTransaction(ReadCommitted);
       // 直接用 row.Data（CreateTeacherRequest）呼叫 CreateTeacherAsync
       INSERT MT_Users → 取得 UserId
       生成 TeacherCode → INSERT MT_Teachers
       寫 MT_AuditLogs（見下方規範）
       tx.Commit();
       results.Add(new BatchImportRowResult { RowNumber = row.RowNumber, IsSuccess = true });
     }
     catch (SqlException ex) when (ex.Number is 2601 or 2627)
     {
       results.Add(new BatchImportRowResult { RowNumber = row.RowNumber, IsSuccess = false,
                   ErrorMessage = "Email 信箱已存在" });
       // 不 rethrow，繼續下一筆
     }
     catch (Exception ex)
     {
       results.Add(new BatchImportRowResult { RowNumber = row.RowNumber, IsSuccess = false,
                   ErrorMessage = ex.Message });
       // 不 rethrow，繼續下一筆
     }
   ```
4. 回傳 `results`；UI 端 `results.Count(r => r.IsSuccess)` / `results.Count(r => !r.IsSuccess)` 算計數

**設計理由**：避免因 1 筆錯誤造成 99 筆有效資料全部失敗；同一筆教師的兩個 INSERT（MT_Users + MT_Teachers）仍在小 Transaction 內保持原子性。

**AuditLog 規範（每筆成功建立後寫入）**：
- `Action = 0`（建立）
- `TargetType = 2`（Teacher）
- `ProjectId = NULL`（跨梯次操作）
- `NewValue = { "targetDisplayName": "王小明", "email": "...", "teacherCode": "T115001" }`
- 失敗的列不寫 AuditLog

**預設密碼**：`CSF@01024304`（與現有 `DefaultTeacherPassword` 常數相同），呼叫 `AuthService.HashPassword`，`IsFirstLogin = 1`。

---

## 五、Model 新增類別清單

新增至 `Models/TeacherModels.cs`（**3 個新類別**，復用既有 `CreateTeacherRequest`）：

```csharp
/// <summary>批次匯入 — 每列資料（解析後）</summary>
public class BatchImportRow
{
    public int RowNumber { get; set; }                          // Excel 原始列號（從 2 開始）
    public CreateTeacherRequest Data { get; set; } = new();     // 復用既有 DTO，涵蓋全部 13 欄
    public BatchImportRowStatus Status { get; set; }
    public List<string> Errors { get; set; } = new();           // 紅色錯誤訊息（不可匯入）
    public List<string> Warnings { get; set; } = new();         // 橘色警告訊息（DB Email 已存在）
    public bool IsSelected { get; set; } = true;                 // 使用者勾選狀態
}

/// <summary>批次匯入 — 列的驗證狀態（三色 UI 對應）</summary>
public enum BatchImportRowStatus
{
    Valid   = 0,    // 驗證通過，可匯入（綠色）
    Warning = 1,    // DB Email 已存在，預設勾選可嘗試匯入（橘色）
    Error   = 2     // 格式/必填/檔內重複錯誤，強制排除（紅色）
}

/// <summary>批次匯入 — 每筆匯入結果明細（List 整體為 ImportTeachersAsync 回傳值）</summary>
public class BatchImportRowResult
{
    public int RowNumber { get; set; }
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
}
```

**復用 `CreateTeacherRequest` 的好處**：匯入迴圈內可直接將 `row.Data` 傳入現有 `CreateTeacherAsync(row.Data, operatorId)`，無需欄位轉換，避免兩套平行 DTO 長期同步維護的負擔。

---

## 六、三檔案影響範圍

> 本功能嚴格符合三檔案規則，**不新增任何額外檔案**（無需 `Endpoints/TeacherImportEndpoints.cs`，亦無需修改 `Program.cs`）。

### `Components/Pages/Teachers.razor` [MODIFY]

新增狀態欄位：
```csharp
private bool showImportSlideOver = false;
private IBrowserFile? importFile;
private List<BatchImportRow>? importRows;   // ParseExcelAsync 回傳
private string? parseError;                 // 工作表不存在等解析失敗訊息
private List<BatchImportRowResult>? importResults; // ImportTeachersAsync 回傳
private bool isParsingFile = false;
private bool isImporting = false;
private string importStep = "upload"; // "upload" | "preview" | "result"
```

新增 Markup：
- `ImportSlideOver` 面板（含三步驟切換：上傳 / 預覽 / 結果）
- 預覽表格（`overflow-x-auto`，逐列顯示驗證狀態）
  - 紅色列：`bg-red-50 text-red-700`，checkbox `disabled`
  - 橘色列：`bg-orange-50 text-orange-700`，checkbox 啟用、預設 `checked`
  - 合法列：`bg-green-50`（或預設底色），checkbox 啟用、預設 `checked`
- InputFile 元件（`@onchange="HandleFileSelected"`）
- `<a href="@($"{NavigationManager.BaseUri}temp/教師資料匯入_公版.xlsx")" download>` 靜態下載連結

**不需要新增 Shared 元件**：匯入流程僅此頁使用，不抽離。

### `Services/TeacherService.cs` [MODIFY]

新增兩個 public 方法（介面 `ITeacherService` 同步新增）：
- `Task<(List<BatchImportRow> Rows, string? ParseError)> ParseExcelAsync(Stream fileStream)`
- `Task<List<BatchImportRowResult>> ImportTeachersAsync(IReadOnlyList<BatchImportRow> rows, int operatorId)`

新增私有 helper：
- `static int ParseGender(string? raw)` — 「男」→ 1，「女」→ 2，其他 → 0
- `static int? ParseEducation(string? raw)` — 「學士」→ 1，「碩士」→ 2，「博士」→ 3
- `static int ParseStatus(string? raw)` — 「啟用」→ 1，「停用」→ 0，其他 → -1（無效）
- `static string GetCellString(IRow row, int colIdx)` — 安全讀取 NPOI 儲存格值

### `Models/TeacherModels.cs` [MODIFY]

新增三個類別（詳見第五節）：`BatchImportRow`、`BatchImportRowStatus`、`BatchImportRowResult`。

---

## 七、錯誤處理與邊界案例

| 案例 | 處理方式 |
|---|---|
| 檔案超過 5MB | 上傳前在 Blazor 端判斷 `file.Size > 5_000_000`，顯示錯誤提示，不呼叫 Service |
| 非 `.xlsx` 格式 | 同上，檢查副檔名與 MIME |
| 工作表不叫「教師資料匯入區」 | `ParseExcelAsync` 回傳 `([], "Excel 格式不符，請下載公版範本重新填寫")`，UI 顯示 `parseError` 文字 |
| 欄位數量不足（少於 13 欄） | 缺失欄位視為空白，由必填驗證攔截 |
| A 欄序號斷開（空列中間有資料） | 讀 B~N 欄，若整列全空白才停止；序號不影響讀取邏輯 |
| 橘色警告列使用者保留勾選，寫入撞 UNIQUE | catch `SqlException 2601/2627` → 記錄「Email 信箱已存在」失敗訊息，繼續下一筆 |
| 某筆中途 MT_Teachers INSERT 失敗 | 小 Transaction rollback（僅這一筆），其他筆不受影響 |
| 50 筆以上資料 | 逐筆 INSERT；預期 50 筆約 0.5~2 秒（PBKDF2 雜湊每筆 ~10ms 是瓶頸）|
| `預設教師` 角色不存在 | 整批失敗前拋出，顯示「系統尚未建立「預設教師」角色，請先至角色管理建立」（與現有邏輯相同） |

---

## 八、效能考量

### 批次寫入策略
- **本次選擇：逐筆 INSERT（Single INSERT）**，不使用 Bulk Insert / TVP
- **理由**：
  1. 預期單次匯入量 ≤ 100 筆（教師人才庫不是大量操作場景）
  2. 每筆需呼叫 `AuthService.HashPassword`（PBKDF2 100,000 次迭代，約 10-50ms/筆），Bulk Insert 省不了這個時間
  3. 逐筆可獨立記錄每列成功/失敗，Bulk Insert 難以細分錯誤
  4. 避免引入 `Dapper.Plus` 等第三方套件

- **100 筆估算**：PBKDF2 50ms × 100 = 5 秒（最慢情境）。若使用者覺得太慢，後續可改成平行化 Hash 計算（CPU-bound，適合 `Parallel.ForEach`）。

### 預覽效能
- 批次查詢 DB Email：單一 `WHERE Email IN (...)` 查詢，不是 N+1

---

## 九、與既有功能的整合

### 不自動加入當前梯次
匯入的教師只進人才庫（MT_Teachers），**不自動加入任何梯次**。加入梯次走既有「參與專案管理 Tab」的「加入梯次」Modal，維持現有 UI 一致性。

### IsFirstLogin 流程銜接
`IsFirstLogin = 1`，教師首次登入時被 `Login.razor` 的 `IsFirstLogin Claim` 強制導向 `/first-login-password` 要求修改密碼，與現有流程完全相同。

### UNIQUE 約束 catch 翻譯
DB 寫入若觸發 `SqlException 2601/2627`，翻譯成人話（與 `CreateTeacherAsync` 既有邏輯相同），不 rethrow，繼續下一筆。

### 「下載公版範本」連結

使用者已確認：**公版範本無敏感資料（僅空白欄位範本），可公開下載，不需授權保護**，採靜態檔案方案。

**檔案放置**：`wwwroot/temp/教師資料匯入_公版.xlsx`

> 發佈前請將 `D:\jay_liu\Desktop\教師資料匯入_公版.xlsx` 複製至 `D:\IISWebSize\MT\wwwroot\temp\教師資料匯入_公版.xlsx`。`dotnet publish` 不會自動複製此檔，每次發佈後需確認檔案存在於部署目標的 `wwwroot/temp/` 目錄下。

**UI 觸發**：Teachers.razor 中使用 `<a>` 標籤直接連結，**不需要 forceLoad / NavigateTo**：

```razor
@* PathBase（IIS 子應用程式 /MT 前綴）透過 NavigationManager.BaseUri 自動帶入 *@
<a href="@($"{NavigationManager.BaseUri}temp/教師資料匯入_公版.xlsx")"
   download="教師資料匯入_公版.xlsx"
   class="...tailwind classes...">
    下載公版範本
</a>
```

`NavigationManager.BaseUri` 在本機為 `https://localhost:port/`，在 IIS `/MT` 子應用程式為 `https://host/MT/`，前綴自動正確。

---

## 十、Verification Plan

| 測試案例 | 步驟 | 預期結果 |
|---|---|---|
| 下載公版範本 | 點擊「下載公版範本」連結 | 瀏覽器直接下載 `教師資料匯入_公版.xlsx`（本機與 IIS `/MT` 子應用程式路徑均正確） |
| 公版範本成功匯入 | 填好 5 筆正確資料上傳 | 預覽顯示 5 列全綠，匯入後 DB 新增 5 筆教師，Teachers 列表立即更新 |
| 重複 Email 偵測（檔內） | 同一 xlsx 填兩筆相同 Email | 第二筆標為**紅色**錯誤列，checkbox 禁用，無法勾選 |
| 重複 Email 偵測（DB 已存在） | 填一筆 DB 中已有的 Email | 標為**橘色**警告列，預設勾選；取消勾選可跳過；保留勾選後匯入 → Step C 結果摘要顯示該列失敗「Email 信箱已存在」 |
| 橘色列取消勾選 | 手動取消橘色列 checkbox | 底部「確認匯入 N 筆」的 N 即時減少 1 |
| 必填欄位空白 | 姓名留空 / 信箱留空 | 對應列標為**紅色**錯誤，顯示對應錯誤訊息 |
| Email 格式錯誤 | 填 `notanemail` | 對應列標為**紅色**錯誤，顯示「信箱格式不正確」 |
| 非 xlsx 格式 | 上傳 .xls 或 .csv | 上傳前攔截，顯示格式不支援提示 |
| 工作表名稱錯誤 | 上傳改過工作表名的 xlsx | 顯示「Excel 格式不符，請下載公版範本重新填寫」 |
| 部分欄位空白容忍 | 只填必填 4 欄，其餘空白 | 解析成功，選填欄位存為 null |
| 混合列（部分成功部分失敗） | 3 筆合法 + 1 筆 DB 撞 UNIQUE（橘色勾選） | Step C 顯示「成功 3 筆，失敗 1 筆」，失敗明細列出 RowNumber + 錯誤訊息 |
| 大量資料（50 筆） | 準備 50 筆測試資料 | 在 10 秒內完成匯入，無 timeout；顯示正確計數 |
| IsFirstLogin 整合 | 匯入 1 筆 → 以該帳號登入 | 自動導向 `/first-login-password`，強制修改密碼 |
| AuditLog 記錄 | 匯入 3 筆後查 MT_AuditLogs | 3 筆 Create 紀錄，Action=0, TargetType=2, ProjectId=NULL，NewValue 含 `targetDisplayName` |
| 失敗列不寫 AuditLog | 匯入含 1 筆 UNIQUE 衝突列 | MT_AuditLogs 只新增成功筆數，失敗列無對應記錄 |
| `dotnet build` | 實作完成後 | 0 警告 0 錯誤 |

---

## 十一、實作優先順序

建議依下列 commit 切點分批實作，每個階段獨立可 build、可 review：

| 階段 | 內容 | 對應檔案 |
|---|---|---|
| ① DTO + 解析方法 | 新增 `TeacherModels.cs` **3 個類別**（`BatchImportRow`、`BatchImportRowStatus`、`BatchImportRowResult`）+ `TeacherService.ParseExcelAsync`（回傳 tuple）+ 4 個 private helper | `TeacherModels.cs`、`TeacherService.cs`、`ITeacherService` 介面 |
| ② UI Modal + 預覽表格 | `Teachers.razor` 新增 Slide-over 面板（Step A / Step B Markup，含三色預覽表格、勾選邏輯、統計列、底部按鈕），接線至 `ParseExcelAsync` | `Teachers.razor` |
| ③ 寫入 + Audit Log | `TeacherService.ImportTeachersAsync`（逐筆 Transaction、UNIQUE catch 翻譯、Audit Log 寫入），接線至 Step C 結果摘要 | `TeacherService.cs`、`Teachers.razor`（Step C） |
| ④ Verification | `dotnet build` 通過 + 瀏覽器走完所有 Verification Plan 測試案例 | — |

---

## 十二、後續可改善項目（非本次範圍）

- 支援 `.xls` 舊格式（需引入 NPOI `HSSFWorkbook`）
- 預覽表格提供欄位錯誤的 inline 編輯（減少下載-修改-重傳往返）
- PBKDF2 Hash 平行化（`Parallel.ForEach`）加速大量匯入
- 匯入後提供「下載匯入結果報表」（Excel/CSV）
- 若未來有題目功能也需要批次匯入，可將 Excel 解析 helper 抽到共用工具類別
