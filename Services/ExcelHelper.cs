using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace MT.Services;

/// <summary>
/// 通用 Excel 讀寫工具（封裝 NPOI 細節）。
/// 業務邏輯（如「男→1」、「啟用→1」）不放在這裡，請放呼叫端 Service。
/// </summary>
public static class ExcelHelper
{
    /// <summary>
    /// 讀取單一工作表的指定欄位範圍。整列空白即停止讀取。
    /// </summary>
    /// <param name="stream">Excel 檔案串流（.xlsx）</param>
    /// <param name="sheetName">工作表名稱</param>
    /// <param name="startColIdx">起始欄 index（0-based，例如 B 欄 = 1）</param>
    /// <param name="endColIdx">結束欄 index（含；例如 N 欄 = 13）</param>
    /// <param name="startRowIdx">起始列 index（0-based，例如第 2 列 = 1，跳過標題）</param>
    /// <returns>
    /// (Rows, Error)。每列為 string 陣列，長度 = endColIdx - startColIdx + 1，內容已 Trim。
    /// 錯誤情境（檔案損毀、工作表不存在）回 (empty list, 錯誤訊息)。
    /// </returns>
    public static (List<string[]> Rows, string? Error) ReadSheet(
        Stream stream,
        string sheetName,
        int startColIdx,
        int endColIdx,
        int startRowIdx)
    {
        XSSFWorkbook workbook;
        try
        {
            workbook = new XSSFWorkbook(stream);
        }
        catch
        {
            return ([], "Excel 檔案損毀或格式不支援");
        }

        var sheet = workbook.GetSheet(sheetName);
        if (sheet is null)
            return ([], $"Excel 格式不符，請下載公版範本重新填寫（找不到「{sheetName}」工作表）");

        var rows = new List<string[]>();
        int colCount = endColIdx - startColIdx + 1;

        for (int rowIdx = startRowIdx; rowIdx <= sheet.LastRowNum; rowIdx++)
        {
            var sheetRow = sheet.GetRow(rowIdx);

            // 整列空白即停止
            bool allEmpty = true;
            for (int ci = startColIdx; ci <= endColIdx; ci++)
            {
                if (GetCellString(sheetRow, ci) != "")
                {
                    allEmpty = false;
                    break;
                }
            }
            if (allEmpty) break;

            var cells = new string[colCount];
            for (int ci = startColIdx; ci <= endColIdx; ci++)
                cells[ci - startColIdx] = GetCellString(sheetRow, ci);
            rows.Add(cells);
        }

        return (rows, null);
    }

    /// <summary>
    /// 安全讀取儲存格為字串。處理 null cell、不同 CellType（String/Numeric/Boolean/Formula）。
    /// 數字若無小數位回傳整數字串（"10" 而非 "10.0"）。回傳前已 Trim。
    /// </summary>
    public static string GetCellString(IRow? row, int colIdx)
    {
        if (row is null) return "";
        var cell = row.GetCell(colIdx);
        if (cell is null) return "";

        var raw = cell.CellType switch
        {
            CellType.String  => cell.StringCellValue,
            CellType.Numeric => cell.NumericCellValue % 1 == 0
                ? ((long)cell.NumericCellValue).ToString()
                : cell.NumericCellValue.ToString(),
            CellType.Boolean => cell.BooleanCellValue.ToString(),
            CellType.Formula => cell.CachedFormulaResultType switch
            {
                CellType.String  => cell.StringCellValue,
                CellType.Numeric => ((long)cell.NumericCellValue).ToString(),
                _                => ""
            },
            _ => ""
        };

        return raw?.Trim() ?? "";
    }
}
