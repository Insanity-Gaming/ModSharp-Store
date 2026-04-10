using SqlSugar;

namespace InsanityGaming.Store.Models;

[SugarTable("users")]
public class Users
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public ulong Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public uint Credits { get; set; } = 0;
}
