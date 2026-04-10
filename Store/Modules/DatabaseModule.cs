using InsanityGaming.Store.Interfaces;
using InsanityGaming.Store.Models;
using SqlSugar;

namespace InsanityGaming.Store.Modules;

public class DatabaseModule : IModule
{
    private readonly ISqlSugarClient _db;

    public DatabaseModule(ISqlSugarClient db)
    {
        _db = db;
    }

    public bool Init()
    {
        _db.CodeFirst.InitTables<Users, Inventory, Equipment>();
        return true;
    }
}
