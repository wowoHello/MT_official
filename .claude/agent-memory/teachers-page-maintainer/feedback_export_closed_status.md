---
name: 匯出採用/不採用欄位的結案判斷規則
description: 匯出 Excel 時，採用題數/不採用題數須依梯次是否結案決定顯示內容
type: feedback
---

未結案梯次匯出 Excel 時，「採用題數」與「不採用題數」兩欄一律顯示全形破折號「－」，**不顯示 0**。

**Why:** 採用/不採用是結案時才產生的最終判決（Status=12/11），未結案前數值恆為 0，顯示 0 會讓使用者誤以為「真的零題採用」，造成誤判。只有 ClosedAt IS NOT NULL（手動點按「結案入庫」後寫入）才顯示實際數字（包含 0）。

**How to apply:**
- 結案判斷依據：`MT_Projects.ClosedAt IS NOT NULL`（非 Status 欄位）
- 影響範圍：CWT 命題側（AssembleCwtCells）、CWT 審題側（AssembleCwtReviewCells）、LCT 命題側（AssembleLctComposeCells）、LCT 審題側（AssembleLctReviewCells）四個 Assemble 方法
- 實作方式：`ExportProjectMeta` 補 `ClosedAt` 欄位，四個 Build 方法與 Assemble 方法各加 `bool isClosed` 參數
- 字元：全形破折號「－」（U+FF0D），與既有「無分母/無分配」的 fallback 字元一致
- 管理身分列（AdminRoles）本來就全部顯示「－」，不受此邏輯影響
