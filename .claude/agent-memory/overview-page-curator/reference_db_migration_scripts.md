---
name: 與 Overview 相關的 DB migration 腳本位置
description: 影響 Overview 查詢的 DB 變動腳本與部署狀態速查
type: reference
---

**目錄**：`D:\IISWebSize\MT\.claude\rules\sql\`

**CWT/LCT 雙模式相關（2026-05-21）**：
- `migrate_project_type_and_granularity.sql` — MT_Projects.ProjectType + MT_ProjectTargets.Granularity（**已部署**，含資料表重置）
- `migrate_project_exam_level.sql` — MT_Projects.ExamLevel（**待執行** / 已部署？腳本標注「待執行」但 MT_DB.sql 已含此欄位 → 推測已部署）
- `fix_granularity_description.sql` — Granularity 欄位描述修正（程式只用 0/1，原描述錯寫 0/1/2）
- `migrate_memberquotas_granularity.sql` — MT_MemberQuotas 加 Granularity

**第一波 / 第二波效能改善（影響 Overview SQL 查詢）**：
- `create_vw_QuestionRoundStartedAt.sql` — Overview 4 處 SQL 已採用此 view 消除 MAX subquery 重複
- `add_unique_constraints.sql` + `add_missing_unique_constraints.sql` — 6 個 UNIQUE 索引（不直接影響 Overview，但 RoleService/MembershipService 已整合）
- `add_indexes_phase2_login_token.sql` — LoginLogs 複合索引（無關 Overview）
- `add_review_assignments_unique_indexes.sql` — MT_ReviewAssignments 唯一索引（影響 GetAllReviewersRespondedAsync 等審題單元判定）

**權威 schema dump**：`D:\MTrefer\MT_DB.sql`（2026-05-21 16:54 dump，含 ProjectType + ExamLevel + Granularity）

**How to apply**：未來改 Overview SQL 之前先看 MT_DB.sql 確認欄位真實存在；新增功能涉及 ProjectType 過濾時，記得同步 vw_QuestionRoundStartedAt 是否需要加 ProjectType 維度（目前 view 不含此條件，跨梯次彙整功能若上線需重新評估）。
