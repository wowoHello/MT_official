---
name: 公告模組兩層級權限尚未實作（2026-05-08 確認仍為 gap）
description: cwt-roles-rules.md 要求第 8 模組有「僅瀏覽/瀏覽與編輯」兩層，目前 RolePermissionToggle 只有 bool IsEnabled，未做第二層
type: project
---

`cwt-roles-rules.md` 規格要求系統公告/使用說明模組（ModuleKey: announcements）在 Toggle 開啟後，應展開次層選單讓管理員選「僅瀏覽」或「瀏覽與編輯」。

2026-05-08 重新確認：`RolePermissionToggle` 類別（RoleModels.cs 第 109-121 行）與 `RolePermissionInput` 類別（第 143-147 行）均只有 `bool IsEnabled`，沒有 AnnouncementAccess 欄位。Roles.razor 的 Toggle 渲染迴圈（第 595-613 行）對所有模組一視同仁，沒有特別處理 announcements ModuleKey。

**Why:** 命題教師、總召等外部/內部預設角色對公告僅有瀏覽權，系統管理員有完整編輯權，需要資料庫層面區分。

**How to apply:**
- 需擴充 MT_RolePermissions 加入 `AnnouncementAccess TINYINT` 欄位（0=無、1=僅瀏覽、2=瀏覽與編輯）
- 更新 `RolePermissionToggle`、`RolePermissionInput` 加入 `AnnouncementAccess int?` 欄位
- Roles.razor 角色 Modal 的 Toggle 清單對 `t.ModuleKey == "announcements"` 項目在 IsEnabled 為 true 時展開 radio 選擇
- Code Review 編號：R-001（高優先級）
