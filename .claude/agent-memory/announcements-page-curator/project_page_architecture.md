---
name: Announcements 頁面架構現況
description: 2026-05-13 二次讀碼完整驗證，三檔案結構符合規則，P1/P2 缺口確認仍未解決
type: project
---

## 三檔案結構（符合規則）
- `Components/Pages/Announcements.razor` — UI + @code 邏輯（約 884 行）
- `Services/AnnouncementService.cs` — 商業邏輯 + Dapper DB 存取
- `Models/AnnouncementModels.cs` — Enum、DTO、表單模型

## 狀態設計（重要：DB 只有兩種狀態）
- DB `MT_Announcements.Status` TINYINT：0=草稿、1=發佈
- **沒有 Status=2**，「已下架」是前端 `DisplayStatus` 屬性由日期推導：
  - Status=0 → 草稿
  - Status=1 + 今天 < PublishDate → 未發佈
  - Status=1 + 在有效期間 → 已發佈
  - Status=1 + 今天 > UnpublishDate → 已下架

## 分類（Category TINYINT，4 種）
| 值 | 名稱 | Tailwind 顏色 |
|---|------|-------------|
| 1 | 系統公告 | `bg-blue-100 text-blue-700` |
| 2 | 命題公告 | `bg-emerald-100 text-emerald-700` |
| 3 | 審題公告 | `bg-purple-100 text-purple-700` |
| 4 | 其它 | `bg-gray-100 text-gray-600` |

## 統計卡片（5 張，前端計算）
`ComputedStats` 屬性即時從 `announcements` 清單計算：
公告總數、已發佈、草稿、已下架、置頂中（僅計 IsPinned=true AND DisplayStatus="已發佈"）。

## 篩選（三維，FilteredList 屬性）
1. 關鍵字（搜尋標題 + Content）— `DebouncedSearchInput` 元件
2. 分類 — `<select>` 對應 Category 值 0~4
3. 顯示狀態 — `<select>` 對應 DisplayStatus 字串（all/已發佈/未發佈/草稿/已下架）

**注意**：狀態篩選有「未發佈」選項（目前規格書 cwt-ac-rules.md 未提及，但實際實作有）。

## 排序規則（FilteredList 內）
`置頂 DESC > DisplayStatus (已發佈=0/未發佈=1/草稿=2/已下架=3) > Category ASC > PublishDate DESC`

**與 cwt-ac-rules.md 規格差異**：規格書說「已發佈 > 草稿 > 已下架」，實作多了「未發佈」（排序值=1，介於已發佈和草稿之間）。

## 列表欄位（12 欄 grid）
| 欄位 | 說明 |
|------|------|
| 置頂 | fa-thumbtack 圖示（col-span-1） |
| 分類 | 彩色 Badge（col-span-1） |
| 公告標題 | 含 TOP Badge + 標題 + HTML 摘要（最多 60 字）（lg:col-span-5） |
| 綁定梯次 | 全站廣播 or 梯次名稱（hidden lg:block lg:col-span-2） |
| 狀態 | `StatusBadge` 元件 Compact 模式（col-span-2 md:col-span-1） |
| 發佈日 | yyyy/MM/dd HH:mm（hidden md:block） |
| 操作 | 編輯(筆) + 置頂(圖釘，僅已發佈/未發佈顯示) + 刪除(垃圾桶) |

點擊整行觸發 `OpenViewModal`（檢視 Modal），操作按鈕用 `@onclick:stopPropagation` 阻止冒泡。

## SlideOver 面板表單
- 使用 `CustomModal` 元件，`Type=SlideOver`，`MaxWidth=max-w-2xl`
- `KeepAlive="true"` + `CloseOnBackdropClick="true"` + `OnBackdropClose="HandleBackdropSaveDraft"`
- 背景點擊：若標題/PublishDate/Content 皆已填，自動存為草稿
- **表單無狀態下拉**（P2 缺口），Status 由「存為草稿」或「儲存公告」按鈕決定
- 表單欄位：分類(InputSelect) + 綁定梯次(原生select) + 上架日(datetime-local) + 下架日(datetime-local) + 置頂(InputCheckbox) + 標題(InputText) + 內容(`InlineQuillEditor`)
- 日期即時防呆：上架日改為晚於下架日 → 清空下架日

## 檢視 Modal
- `CustomModal` 元件，`Type=Center`
- 顯示：分類 Badge + StatusBadge + 置頂標籤 + 標題 + 元資料（發佈日/下架日/梯次）+ HTML 內容（`@((MarkupString)content)`）
- 頁腳：「編輯此公告」按鈕（呼叫 ViewToEdit，先關 Modal 再開 SlideOver）

## 生命週期
- `OnParametersSetAsync`（非 OnInitializedAsync）— 因 CascadingParameter ModuleCards 異步載入
- 流程：`ModuleCards is null` → 等待 → `HasAnnouncementPermission()` 失敗 → 導回 `/` → 通過 → `RunAnnouncementMaintenanceAsync` → `LoadDataAsync`
- `hasInitialized` flag 防止重複載入

## Service 方法清單
| 方法 | 說明 |
|------|------|
| `GetAnnouncementListAsync()` | 全部公告（含 AuthorName、ProjectName JOIN） |
| `GetAnnouncementEditAsync(id)` | 單筆編輯 DTO |
| `GetProjectDropdownAsync()` | 梯次下拉（IsDeleted=0，Year DESC） |
| `CreateAsync(model, userId)` | 新增 + AuditLog（transaction） |
| `UpdateAsync(id, model, userId)` | 更新 + AuditLog（transaction） |
| `TogglePinAsync(id, userId)` | 置頂切換 SQL（CASE WHEN）+ AuditLog |
| `AutoUnpinExpiredAsync(userId)` | 批次取消置頂（草稿或已過下架期）+ 批次 AuditLog |
| `DeleteAsync(id, userId)` | 硬刪除（先寫 AuditLog 再 DELETE）+ transaction |
| `GetHomeAnnouncementsAsync(projectId)` | 首頁用，僅已發佈且在期間 |

所有寫入操作前呼叫 `EnsureCanEditAsync`（查 MT_RolePermissions LOWER(ModuleKey)='announcements'）。

## 分頁
- `PageSize = 10`（const）
- `Pagination` 共用元件
- 每次篩選條件變更自動 `currentPage = 1`

## 使用說明手冊
`ShowUserGuide()` 直接呼叫 SweetAlert2 彈窗，內含 3 筆硬編碼 PDF 名稱（未接 DB）。

**Why:** 此為 P1 缺口，待後續接 MT_UserGuideFiles 資料表。

## 與功能介紹文件的對照（2026-05-13 核對）

核對來源：`D:\jay_liu\Desktop\命題系統相關\網站功能介紹.md` 系統公告/使用說明章節。

### 已實作（符合規格）
- 5 張統計卡片（公告總數/已發佈/草稿/已下架/置頂中）：符合
- 三維篩選（關鍵字/分類下拉/狀態下拉）：符合，且狀態下拉含「未發佈」選項（規格書未明列但合理）
- 公告列表 12 欄 grid（置頂/分類/標題/綁定梯次/狀態/發佈日/操作）：符合
- 操作按鈕：編輯 + 置頂切換（僅已發佈/未發佈顯示）+ 刪除：符合
- SlideOver 表單（分類/梯次/上下架日期/置頂/標題/Quill 內容）：符合
- 存為草稿 + 發佈兩個底部按鈕：符合
- 背景點擊自動存草稿：符合
- 自動取消草稿或已下架公告的置頂：符合（AutoUnpinExpiredAsync）
- 首頁公告連動（`GetHomeAnnouncementsAsync`）：符合
- 全站廣播 / 指定梯次：符合
- DB 只存 0/1 狀態，「未發佈」與「已下架」前端推導：符合

### 規格提到但尚未實作（延續 P1/P2 缺口）
- 使用說明手冊按鈕提供實際 PDF 下載清單：目前為假資料彈窗（P1）
- InlineQuillEditor 的中文標點符號快速插入列（12 個）：缺 UI（P2）

### 注意事項
- 功能介紹文件指出操作按鈕包含「置頂 / 取消置頂（僅已發佈 / 未發佈才有）」，
  程式碼實作為 `ann.DisplayStatus is "已發佈" or "未發佈"` 判斷，與文件一致。
- 首頁公告看板連動已透過 `HomeService` 呼叫 `IAnnouncementService.GetHomeAnnouncementsAsync` 實作。
