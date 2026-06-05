namespace MT.Models;

// ======================================================================
// 聘書繪製待辦清單 — server 端 SyncCertificatesAsync 後撈出 FileName IS NULL 的紀錄組成，
// 傳給 JS 端由 Canvas 繪製並上傳。
// camelCase property name 透過 System.Text.Json 自動小寫（JS interop 用），這裡保持 PascalCase。
// ======================================================================
public class AppointmentDraftDto
{
    public int CertId { get; set; }                  // MT_AppointmentCertificates.Id
    public string TargetFileName { get; set; } = ""; // {CertId}_{UserId}_{yyyyMMdd}_{RoleId}.jpg

    // 8 個繪製欄位（對應計畫書 ① ~ ⑧）
    public string CertNumberText { get; set; } = ""; // ① ({Year})中檢(中)聘字第{NNNN}號（4 碼，每民國年從 100 起）
    public string School { get; set; } = "";          // ② 學校名稱
    public string DisplayName { get; set; } = "";     // ③ 姓名
    public string Title { get; set; } = "";           // ③ 教師職稱
    public string RoleName { get; set; } = "";        // ④ 梯次內身份
    public string PeriodText { get; set; } = "";      // ⑤ 聘期
    public int IssuedYearROC { get; set; }            // ⑥ 任命年（民國）
    public int IssuedMonth { get; set; }              // ⑦ 任命月
    public int IssuedDay { get; set; }                // ⑧ 任命日
}

/// <summary>
/// 教師詳細頁「參與專案」分頁的下載按鈕清單項目。
/// 也用於導覽列使用者頭像下拉選單裡的「下載聘書」按鈕。
/// </summary>
public class AppointmentDownloadItem
{
    public int CertId { get; set; }
    public int ProjectId { get; set; }
    public string ProjectName { get; set; } = "";
    public int RoleId { get; set; }
    public string RoleName { get; set; } = "";
    public string DisplayName { get; set; } = "";    // 受聘教師姓名（給下載檔名前綴用）
    public string CertNumberText { get; set; } = ""; // 完整字號顯示用
    public string FileName { get; set; } = "";       // 不含路徑；前端組 {pathBase}/files/{FileName} 即可下載
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 聘期編輯 Modal 載入用 — 一個 (UserId, ProjectId) 對應的所有 cert 與預設聘期。
/// 預設聘期（DefaultStart/End）取自 MT_Projects.StartDate/EndDate；每張 cert 各有獨立的 Custom 欄位。
/// </summary>
public class CertEditPanelData
{
    public string DisplayName { get; set; } = "";   // 教師姓名
    public string ProjectName { get; set; } = "";   // 梯次名
    public DateTime DefaultStart { get; set; }      // 梯次預設開始
    public DateTime DefaultEnd { get; set; }        // 梯次預設結束
    public List<CertEditInfo> Certs { get; set; } = new();
}

/// <summary>Modal 內每張 cert 的編輯項。CustomStart/End 兩者必須同時為 NULL 或同時有值。</summary>
public class CertEditInfo
{
    public int CertId { get; set; }
    public string RoleName { get; set; } = "";
    public string CertNumberText { get; set; } = "";
    public DateTime? CustomStartDate { get; set; }
    public DateTime? CustomEndDate { get; set; }

    public bool IsCustomized => CustomStartDate.HasValue && CustomEndDate.HasValue;
}

/// <summary>Modal 儲存時批次傳給 Service 的單筆更新。Start/End 兩者必須同時有值或同時為 NULL（成對原則）。</summary>
public record CertPeriodUpdate(int CertId, DateTime? StartDate, DateTime? EndDate);
