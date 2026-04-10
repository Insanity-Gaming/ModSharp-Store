using SqlSugar;

namespace InsanityGaming.Store.Models;

[SugarTable("user_equipment")]
[SugarIndex("uq_store_equip_combo", nameof(Id), OrderByType.Asc, nameof(Unique), OrderByType.Asc, nameof(Slot),
    OrderByType.Asc, nameof(Team), OrderByType.Asc, true)]
public class Equipment
{
    [SugarColumn(IsPrimaryKey = true)]
    public required ulong Id { get; set; }
    [SugarColumn(IsNullable = false)]
    public required string Unique { get; set; }
    [SugarColumn(IsNullable = false)]
    public required int Slot { get; set; }
    [SugarColumn(IsNullable = false)]
    public required int Team { get; set; }
}
