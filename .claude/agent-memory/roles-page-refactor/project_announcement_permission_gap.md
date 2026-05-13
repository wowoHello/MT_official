---
name: 公告模組兩層級權限——已確認非 Gap（2026-05-13 修正）
description: 網站功能介紹.md 明確說明模組權限就是單純的開/關，沒有兩級分權；公告頁面也是統一一個開關
type: project
---

**2026-05-13 重新確認：此條目先前記錄的 R-001 缺口已不成立。**

`D:\jay_liu\Desktop\命題系統相關\網站功能介紹.md`（角色與權限管理章節）明確記載：

> 「功能模組數量是動態的，依 MT_Modules 資料表載入；目前共 8 個模組對應 8 個頁面」
> 「模組權限就是單純的開 / 關，沒有兩級分權；公告頁面也是統一一個開關」

因此 `cwt-roles-rules.md` 中提到的「僅瀏覽 / 瀏覽與編輯兩層」是舊版規格，**已由最新功能介紹文件覆蓋**。

目前實作（RolePermissionToggle 只有 bool IsEnabled）完全符合最新設計。

**DB 層面備注：**
MT_RolePermissions 有 `Permissions TINYINT NOT NULL DEFAULT 1` 欄位（0=檢視/1=編輯），是保留的擴充欄位，目前 UI 層不使用，由 DB 預設值管理。未來若業務需求改變想加回兩層，從這裡開始擴充。

**Why:** 最新功能介紹文件為正式設計依據，優先於舊版規格書。

**How to apply:**
- 不需要對 RolePermissionToggle/RolePermissionInput 加 AnnouncementAccess 欄位
- 若使用者未來要求加回兩層，必須先確認需求後再計畫，不可自行加入
