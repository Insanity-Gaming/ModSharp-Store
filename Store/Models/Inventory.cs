using SqlSugar;

namespace InsanityGaming.Store.Models;

[SugarTable("user_inventory")]
[SugarIndex("uq_store_items_name", nameof(Id), OrderByType.Asc, nameof(Unique), OrderByType.Asc, true)]
public class Inventory
{
    [SugarColumn(IsPrimaryKey = true)]
    public ulong Id { get; set; }
    [SugarColumn(IsNullable = false)]
    public required string Unique { get; set; }
}
