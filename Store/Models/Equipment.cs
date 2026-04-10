using SqlSugar;

namespace InsanityGaming.Store.Models;

[SugarTable("user_equipment")]
[SugarIndex("uq_store_equip_combo", nameof(Id), OrderByType.Asc, nameof(Unique), OrderByType.Asc, nameof(Slot),
    OrderByType.Asc, nameof(Team), OrderByType.Asc, true)]
public class Equipment
{
    [SugarColumn(IsPrimaryKey = true)]
    public ulong Id { get; set; }
    [SugarColumn(IsNullable = false)]
    public string Unique { get; set; } = string.Empty;
    [SugarColumn(IsNullable = false)]
    public int Slot { get; set; }
    [SugarColumn(IsNullable = false)]
    public int Team { get; set; }
}
