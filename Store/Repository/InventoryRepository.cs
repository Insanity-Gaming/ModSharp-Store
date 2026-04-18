using InsanityGaming.Store.Interfaces;
using InsanityGaming.Store.Models;
using SqlSugar;

namespace InsanityGaming.Store.Repository;

public class InventoryRepository : BaseRepository<Inventory>, IInventoryRepository
{
    public InventoryRepository(ISqlSugarClient db) : base(db) { }

    public async Task<List<Inventory>> GetByPlayerAsync(ulong steamId) =>
        await Db.Queryable<Inventory>()
            .Where(i => i.Id == steamId && (i.ExpiresAt == null || i.ExpiresAt > DateTime.UtcNow))
            .ToListAsync();

    public async Task<bool> DeleteByPlayerAsync(ulong steamId) =>
        await Db.Deleteable<Inventory>().Where(i => i.Id == steamId).ExecuteCommandAsync() > 0;

    public async Task<bool> DeleteByPlayerAndUniqueAsync(ulong steamId, string unique) =>
        await Db.Deleteable<Inventory>()
            .Where(i => i.Id == steamId && i.Unique == unique)
            .ExecuteCommandAsync() > 0;

    public async Task<bool> PurgeExpiredAsync() =>
        await Db.Deleteable<Inventory>()
            .Where(i => i.ExpiresAt != null && i.ExpiresAt <= DateTime.UtcNow)
            .ExecuteCommandAsync() > 0;
}
