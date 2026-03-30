namespace MT.Models;

public class ModulePermission
{
    public string ModuleKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "";
    public string PageUrl { get; set; } = "";
    public string Description { get; set; } = "";
    public string ColorClass { get; set; } = "";
    public string BgColorClass { get; set; } = "";
    public bool IsEnabled { get; set; }
    public int AnnouncementPerm { get; set; }
}
