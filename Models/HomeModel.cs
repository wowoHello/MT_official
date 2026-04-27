namespace MT.Models;

// ======================================================================
//  首頁急件 / 到期警示
// ======================================================================

/// <summary>急件警示嚴重度（用於排序與配色）。</summary>
public enum AlertSeverity : byte
{
    /// <summary>橙色：剩餘 3~5 天。</summary>
    Warning = 0,
    /// <summary>紅色：剩餘 0~2 天。</summary>
    Critical = 1
}

/// <summary>急件警示型別。</summary>
public enum AlertType : byte
{
    /// <summary>階段倒數提醒（梯次層級，純資訊）。</summary>
    PhaseCountdown = 0,
    /// <summary>個人任務積壓警示（合併倒數）。</summary>
    PersonalBacklog = 1,
    /// <summary>配額缺口警示（管理員視角，命題階段倒數時觸發）。</summary>
    QuotaGap = 2,
    /// <summary>階段逾期警示（不分角色，紅色 + 跳閃）。</summary>
    PhaseOverdue = 3
}

/// <summary>單筆急件警示資料。</summary>
public class UrgentAlertItem
{
    public AlertType AlertType { get; set; }
    public AlertSeverity Severity { get; set; }
    public int PhaseCode { get; set; }
    public string PhaseName { get; set; } = string.Empty;
    public int DaysLeft { get; set; }

    /// <summary>個人未完成任務數（PhaseCountdown 為 0）。</summary>
    public int PendingCount { get; set; }

    /// <summary>主訊息（首列顯示）。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>副訊息（次列顯示，例如倒數天數）。</summary>
    public string Subtitle { get; set; } = string.Empty;

    /// <summary>點擊跳轉的 URL（含 ?tab= 參數）。</summary>
    public string RedirectUrl { get; set; } = string.Empty;
}
