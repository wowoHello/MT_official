---
name: Status Renumbering v3.0 — SentBack 完全移除
description: 2026-05-05 決策：Status=11(SentBack) 直接刪除，12/13 前移為 11/12，DB migration SQL 已記錄
type: project
---

Status=11（SentBack）已於 2026-05-05 正式完全移除，12→11、13→12。

**Why:** D1 決策廢用 SentBack，v3.0 做 renumbering 清除空號，避免日後誤用與混淆。

**How to apply:** 任何提到「結案未採用」的 SQL 常數用 `QuestionStatus.ClosedNotAdopted = 11`；「結案入庫」用 `QuestionStatus.Archived = 12`。`HistoryTabStatuses = [9, 10, 11, 12]`。TeacherService 中重命名為 `StatusClosedAdopted = 12` / `StatusClosedNotAdopted = 11`，避免與 Adopted=9 / Rejected=10 混淆。

DB migration：若有舊資料 Status=12/13，執行 `UPDATE MT_Questions SET Status = Status - 1 WHERE Status >= 12;`。
