# Agent Memory Index — Roles 頁面重構

- [Roles 頁面當前真實結構](project_roles_page_structure.md) — 5 個檔案、3 個 Modal、SignalR 廣播、DB 資料表對照、關鍵設計決策（2026-05-13 更新）
- [稽核強化已實作](project_audit_enhancement_pending.md) — AuditLogJsonHelper.cs 已確認存在，RoleService 所有 CRUD 已整合 before/after 快照
- [公告模組權限為單純開關](project_announcement_permission_gap.md) — 網站功能介紹.md 確認：模組權限只有 ON/OFF，無兩級分權，舊版 R-001 缺口已不成立
- [Data Annotation 驗證缺失](feedback_missing_data_annotations.md) — EditForm 有 DataAnnotationsValidator 但 Request 類別無 [Required]，驗證機制形同虛設
- [Roles 頁面參考素材來源](reference_roles_prototype_source.md) — 整合文件路徑 D:\MTrefer\pageFinal_doc\Roles.md，舊 html/js 已棄用
