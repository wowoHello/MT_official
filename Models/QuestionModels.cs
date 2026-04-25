namespace MT.Models;

public class QuotaProgressItem
{
    public int QuestionTypeId { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public int Target { get; set; }
    public int Completed { get; set; }
    public int Percent => Target > 0 ? Math.Min(100, Completed * 100 / Target) : 0;
}

public class ProjectPhaseInfo
{
    public int PhaseCode { get; set; }
    public string PhaseName { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int DaysLeft { get; set; }
    public bool IsUrgent => DaysLeft >= 0 && DaysLeft <= 5;
}
