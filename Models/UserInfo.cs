namespace MT.Models;

public class UserInfo
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int RoleId { get; set; }
    public int Status { get; set; }
    public bool IsFirstLogin { get; set; }
    public string RoleName { get; set; } = "";
}
