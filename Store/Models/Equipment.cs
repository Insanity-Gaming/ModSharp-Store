using SqlSugar;

namespace InsanityGaming.Store.Models;

[SugarTable("user_equipment")]
public class Equipment
{
    [SugarColumn(IsPrimaryKey = true)]
    public ulong Id { get; set; }
    [SugarColumn(IsPrimaryKey = true, IsNullable = false)]
    public string ServerId { get; set; } = string.Empty;
    [SugarColumn(IsPrimaryKey = true, IsNullable = false)]
    public int Slot { get; set; }
    [SugarColumn(IsPrimaryKey = true, IsNullable = false)]
    public int Team { get; set; }
    [SugarColumn(IsNullable = false)]
    public string Unique { get; set; } = string.Empty;
}
