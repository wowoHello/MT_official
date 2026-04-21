namespace MT.Models;

/// <summary>
/// 角色身分別標籤資料：同時攜帶名稱與分類（內部/外部），供 UI 統一配色使用。
/// </summary>
/// <param name="Name">角色名稱（如：命題教師、系統管理員）</param>
/// <param name="Category">角色分類 (0:內部, 1:外部)</param>
public record RoleTag(string Name, int Category);
