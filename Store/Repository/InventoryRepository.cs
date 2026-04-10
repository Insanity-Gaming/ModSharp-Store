using InsanityGaming.Store.Interfaces;
using InsanityGaming.Store.Models;
using SqlSugar;

namespace InsanityGaming.Store.Repository;

public class InventoryRepository : BaseRepository<Inventory>, IInventoryRepository
{
    public InventoryRepository(ISqlSugarClient db) : base(db) { }

    public async Task<List<Inventory>> GetByPlayerAsync(ulong steamId) =>
        await Db.Queryable<Inventory>().Where(i => i.Id == steamId).ToListAsync();

    public async Task<bool> DeleteByPlayerAsync(ulong steamId) =>
        await Db.Deleteable<Inventory>().Where(i => i.Id == steamId).ExecuteCommandAsync() > 0;
}
