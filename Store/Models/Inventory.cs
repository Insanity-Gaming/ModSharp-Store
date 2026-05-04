using SqlSugar;

namespace InsanityGaming.Store.Models;

[SugarTable("user_inventory")]
public class Inventory
{
    [SugarColumn(IsPrimaryKey = true)]
    public ulong Id { get; set; }
    [SugarColumn(IsPrimaryKey = true, IsNullable = false)]
    public string Unique { get; set; } = string.Empty;
    [SugarColumn(IsNullable = true)]
    public DateTime? ExpiresAt { get; set; }
}
