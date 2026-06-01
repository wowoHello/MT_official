# 使用說明手冊（以頁面為區分）上傳／預覽計畫書

> **計畫日期**：2026-06-01
> **作者**：Jay 與 Claude 共同設計
> **狀態**：✅ 已完成（2026-06-01）— DB(PageKey 欄+唯一索引+描述) 已部署、3 新檔 + 4 改檔已上線、dotnet build 0 警告 0 錯誤
> **影響範圍**：DB 1 欄位（既有 MT_UserGuideFiles 加 PageKey）+ 新 Service/Model 三檔 + 3 個接觸頁面
> **核心設計**：上傳綁頁面、下載綁既有 RBAC（ModuleCards），共 11 份 PDF，點按一律「新分頁瀏覽器內嵌預覽」（非強制下載）

---

## 一、需求拍板（已確認）

| # | 議題 | 結論 |
|---|---|---|
| Q1 | PDF 儲存 | **公開 wwwroot 靜態檔**（`wwwroot/uploads/guides/{guid}.pdf`） |
| Q2 | 檔案模型 | **一頁一檔，重傳即取代**（11 個固定頁面槽位） |
| Q3 | system-logs 手冊可見性 | **併入「角色與權限管理」(roles) 模組權限** |
| Q4 | 上傳/刪除權限 | **能進到公告頁（模組）即可上傳/刪除**（沿用既有 `HasAnnouncementPermission`，不另設 Edit 檢查） |
| Q5 | 三檔規則歸屬 | **新開 `IUserGuideService` + `UserGuideModels`** |
| Q6 | 儲存記錄方式 | **用既有 `MT_UserGuideFiles` 表**（保留 FileName 原始檔名、FilePath 存 guid 路徑、加 1 欄 PageKey 標頁面身分） |
| Q7 | 點按行為 | **新分頁瀏覽器內嵌預覽**（`target="_blank"`，**不加 `download` 屬性**），使用者要下載再從閱覽器自行下載。Login / Home / Announcements 管理端**三處皆如此** |

---

## 二、資料層變更（既有表加 1 欄）

沿用既有 `MT_UserGuideFiles`，只加「頁面識別鍵」`PageKey`——這是「哪份手冊屬於哪一頁」的唯一載體，`FileName`（任意原始檔名）無法取代它。

```sql
-- .claude/rules/sql/add_userguide_pagekey.sql
ALTER TABLE dbo.MT_UserGuideFiles ADD PageKey NVARCHAR(50) NULL;
GO

-- 一頁一檔：IsActive=1 的列 PageKey 唯一（重傳即取代靠此守門）
CREATE UNIQUE INDEX UQ_MT_UserGuideFiles_PageKey
    ON dbo.MT_UserGuideFiles(PageKey) WHERE IsActive = 1;
GO

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description',
    @value=N'頁面識別鍵：標記此手冊所屬頁面，對應 ModuleCard.PageUrl（login / home / dashboard / projects / overview / cwt-list / reviews / teachers / roles / announcements / system-logs），下載端依此配對使用者權限',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE',  @level1name=N'MT_UserGuideFiles',
    @level2type=N'COLUMN', @level2name=N'PageKey';
GO
```

各欄位用途：
| 欄位 | 存什麼 | 用途 |
|---|---|---|
| `PageKey`（新） | `login` / `home` / `cwt-list`… | 頁面身分（FileName 取代不了） |
| `FileName` | 使用者上傳的原始檔名 | 管理清單顯示 + 預覽分頁標題 |
| `FilePath` | `uploads/guides/{guid}.pdf` | 物理路徑（guid，不寫死，避免覆蓋衝突） |
| `FileSize` | bytes | 清單顯示 |
| `UploadedBy` | UserId | 留痕 |
| `IsActive` | bit | 軟刪 / 取代旗標 |

> 重傳：同 PageKey 舊 active 列 `IsActive=0` + 刪舊物理檔 → 存新 guid 檔 + INSERT 新列。
> 刪除：`IsActive=0` + 刪物理檔。

---

## 三、11 頁 PageKey × 下載可見性

| PageKey | 手冊 | 可見性判定 |
|---|---|---|
| `login` | 登入頁 | **匿名**（登入頁專屬入口） |
| `home` | 首頁 | 所有登入者 |
| `dashboard` | 命題儀表板 | ModuleCard `dashboard` 啟用 |
| `projects` | 命題專案管理 | `projects` 啟用 |
| `overview` | 命題總覽 | `overview` 啟用 |
| `cwt-list` | 命題任務 | `cwt-list` 啟用 |
| `reviews` | 審題任務 | `reviews` 啟用 |
| `teachers` | 教師管理系統 | `teachers` 啟用 |
| `roles` | 角色與權限管理 | `roles` 啟用 |
| `announcements` | 系統公告/使用說明 | `announcements` 啟用 |
| `system-logs` | 系統活動記錄 | **`roles` 啟用** |

下載側比對：`ModuleCards.Any(m => m.PageUrl == file.PageKey && m.IsEnabled)`。`login`/`home` 為特例不查 ModuleCards。11 槽位清單以 Model 端 `GuidePageCatalog` 固定管理（不入表，符合「不常改的文字用 Model」）。

---

## 四、檔案規劃（嚴格三檔）

### 新增（3 檔）

| 檔案 | 用途 | LOC |
|---|---|---|
| `Models/UserGuideModels.cs` | `GuidePageCatalog`（11 槽位：PageKey + 中文頁名 + 權限對應）、`GuideSlotItem`、`GuideViewItem` | ~90 |
| `Services/UserGuideService.cs` | `IUserGuideService`：清單、上傳、刪除、依權限取可見清單；注入 `IWebHostEnvironment` 存實體檔 + `IDatabaseService` 寫 MT_UserGuideFiles | ~190 |
| `.claude/rules/sql/add_userguide_pagekey.sql` | 上述 migration | ~15 |

### 修改（4 檔）

| 檔案 | 修改點 |
|---|---|
| `Program.cs` | 註冊 `AddScoped<IUserGuideService, UserGuideService>()` |
| `Components/Pages/Announcements.razor` | `[使用說明手冊]` 假 Swal（行 1191-1206）→「手冊管理 Modal」（11 槽位 + InputFile 上傳/替換 + 預覽 + 刪除） |
| `Components/Pages/Home.razor` | `[使用說明手冊]` → 依權限預覽清單（`target="_blank"`） |
| `Components/Pages/Login.razor` | 新增「頁面介紹」按鈕 → 開 `login` 手冊預覽（匿名） |

---

## 五、Service 介面

```csharp
public interface IUserGuideService
{
    // 管理端（Announcements）：11 槽位現況（已上傳帶 FileName/FileSize/UploadedAt/Url，未上傳為空）
    Task<IReadOnlyList<GuideSlotItem>> GetManagementSlotsAsync();

    // 上傳/替換：限 PDF + 20MB；舊列 IsActive=0 + 刪舊檔 → 存 guid 檔 + INSERT
    Task UploadAsync(string pageKey, IBrowserFile file, int operatorUserId);

    // 刪除：IsActive=0 + 刪物理檔
    Task DeleteAsync(string pageKey, int operatorUserId);

    // 下載端（Home）：依 ModuleCards 過濾出可見且已上傳的手冊
    Task<IReadOnlyList<GuideViewItem>> GetViewableAsync(IReadOnlyList<UserModuleCard> moduleCards);

    // 登入頁專用：取 login 手冊（匿名，回 Url 或 null）
    Task<GuideViewItem?> GetLoginGuideAsync();
}
```

- **上傳**：`<InputFile>`（accept=".pdf"）→ `IBrowserFile`，前端先驗副檔名 + `file.Size <= 20MB`；寫 `wwwroot/uploads/guides/{guid}.pdf`（`Directory.CreateDirectory` 確保資料夾在）；`FilePath` 存 `uploads/guides/{guid}.pdf`、`FileName` 存原始名。零新增 endpoint。
- **預覽 URL**：`{BaseUri}{FilePath}?v={LastWriteTicks}`（`?v=` 破快取，避免取代後瀏覽器抓到舊 PDF）。

---

## 六、點按預覽機制（三處一致）

PDF 放 wwwroot 當靜態檔送，靜態檔中介軟體預設 `.pdf → application/pdf` 且不加 `Content-Disposition: attachment` → 瀏覽器內建閱覽器**內嵌預覽**，伺服器端零設定。

```razor
@* Login / Home / Announcements 管理端 預覽連結 — 不加 download 屬性 *@
<a href="@item.PreviewUrl" target="_blank" rel="noopener" class="...">
    <i class="fa-solid fa-file-pdf text-red-500"></i> @item.DisplayTitle
</a>
```

> 佐證：Quill 圖片亦 runtime 寫 wwwroot/uploads 再以靜態檔正常顯示 → runtime 靜態檔服務本就通，PDF 同理。

---

## 七、權限守門

- **上傳/刪除**：入口在 Announcements 頁，`OnParametersSetAsync` 已 `HasAnnouncementPermission()` 把關（Q4：能進頁就能管理）。
- **下載/預覽**：Home 依 `GetViewableAsync(moduleCards)` 過濾，外部教師只見自己有權限頁的手冊（呼應 [[project_external_user_permissions]]，不引導禁止頁）。
- **登入頁**：`login` 手冊匿名可預覽（檔案公開於 wwwroot，語意本就對外）。

---

## 八、AuditLog

手冊上傳/刪除屬跨梯次行政操作 → 寫 `MT_AuditLogs`：`ProjectId = NULL`、`Action` 0（上傳=建立）/ 2（刪除）、`TargetType` 取既有最接近值（實作時確認 enum，**不為此新增 enum 值**）、`NewValue`/`OldValue` 含 `targetDisplayName`（中文頁名 + 檔名）。

---

## 九、UI 草案

### 9.1 Announcements 手冊管理 Modal
```
┌─ 使用說明手冊管理 ─────────────────────────────────────┐
│  頁面          檔名                大小   上傳日   操作          │
│  登入頁        登入操作說明.pdf     1.2MB  06/01   預覽 替換 刪除 │
│  首頁          (未上傳)             —      —       上傳           │
│  命題任務      命題操作手冊v3.pdf   2.4MB  05/30   預覽 替換 刪除 │
│  ...（11 列固定）                                              │
└──────────────────────────────────────────────────────┘
```
未上傳列：灰字「未上傳」+ 僅「上傳」鈕；已上傳列：原始檔名/大小/上傳日 +「預覽 / 替換 / 刪除」。上傳/替換用 `<InputFile accept=".pdf">`，選檔即上傳並重載清單。

### 9.2 Home 預覽清單
依權限過濾後列出「📕 命題任務使用手冊」等，每項 `<a target="_blank">` 開預覽。0 筆顯示「目前尚無可用的手冊」。

### 9.3 Login 頁面介紹按鈕
登入卡片附近次要按鈕「📖 頁面介紹」→ 有 `login` 手冊則新分頁預覽，無則 disabled / 提示尚未提供。

---

## 十、影響檔案 + LOC

| 檔案 | 改動 | LOC |
|---|---|---|
| `.claude/rules/sql/add_userguide_pagekey.sql` | 新檔 | ~15 |
| `Models/UserGuideModels.cs` | 新檔 | ~90 |
| `Services/UserGuideService.cs` | 新檔 | ~190 |
| `Program.cs` | DI 1 行 | ~1 |
| `Components/Pages/Announcements.razor` | 假 Swal → 管理 Modal | ~150 |
| `Components/Pages/Home.razor` | 預覽清單 | ~60 |
| `Components/Pages/Login.razor` | 頁面介紹按鈕 | ~30 |
| **合計** | | **~536** |

---

## 十一、Verification Plan

| # | 案例 | 步驟 | 預期 |
|---|---|---|---|
| 1 | Migration | SSMS 跑 add_userguide_pagekey.sql | PageKey 欄 + 唯一索引 + 描述建立 |
| 2 | 上傳 | 管理 Modal 對「命題任務」上傳 PDF | wwwroot/uploads/guides 出現 guid 檔、DB 列含原始 FileName、清單轉「已上傳」 |
| 3 | 替換 | 同頁再上傳 | 舊 guid 檔刪、舊列 IsActive=0、新列生效、清單顯示新原始檔名 |
| 4 | 刪除 | 點刪除 | IsActive=0 + 物理檔刪、清單轉「未上傳」 |
| 5 | 預覽（管理端） | 點「預覽」 | **新分頁瀏覽器內嵌渲染 PDF**（非下載） |
| 6 | 非 PDF / 超大檔 | 上傳 .docx 或 >20MB | 前端攔截提示 |
| 7 | 命題教師預覽 | 命題教師登入 → 首頁手冊 | 只見「命題任務」「審題任務」，點按新分頁預覽 |
| 8 | 審題委員預覽 | 審題委員登入 → 首頁手冊 | 只見「審題任務」 |
| 9 | system-logs 手冊 | 有/無 roles 權限者 | 前者首頁看得到、後者看不到 |
| 10 | 登入頁手冊 | 未登入 → 登入頁「頁面介紹」 | 匿名新分頁預覽 login 手冊 |
| 11 | 未上傳頁不顯示 | 某頁未上傳 | 首頁/登入頁不出現破連結 |
| 12 | 取代後不抓舊檔 | 替換後立即預覽 | `?v=` 破快取，顯示新 PDF |
| 13 | Build | dotnet build | 0 警告 0 錯誤 |

---

## 十二、後續可改善（非本次範圍）

- 受控下載端點（未來若手冊需真正權限阻擋，改 wwwroot 外 + minimal API）
- 手冊版本歷史（目前一頁一檔取代）
- 管理清單進 IMemoryCache（低頻變動）

---

**設計全部拍板。OK 後動工：DB migration 由 user 於 SSMS 執行，程式碼分段 commit + dotnet build 驗證。**
