---
name: 公告模組兩層級權限待實作
description: cwt-roles-rules.md 要求第 8 模組有「僅瀏覽/瀏覽與編輯」兩層，目前資料庫僅存布林值 IsEnabled，尚未實作
type: project
---

`cwt-roles-rules.md` 規格要求系統公告/使用說明模組（ModuleKey: announcements）在 Toggle 開啟後，應展開次層選單讓管理員選「僅瀏覽」或「瀏覽與編輯」。

**Why:** 命題教師、總召等外部/內部預設角色對公告僅有瀏覽權，系統管理員有完整編輯權，需要資料庫層面區分。

**How to apply:**
- 需擴充 MT_RolePermissions 加入 `AnnouncementAccess TINYINT` 欄位（0=無、1=僅瀏覽、2=瀏覽與編輯）
- 更新 `RolePermissionToggle`、`RolePermissionInput` 加入對應欄位
- Roles.razor 角色 Modal 的 Toggle 清單對 ModuleKey == "announcements" 項目顯示次層 radio 或 select
- Code Review 編號：R-001（高優先級）
