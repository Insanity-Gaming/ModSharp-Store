using InsanityGaming.Store.Interfaces;
using InsanityGaming.Store.Models;
using SqlSugar;

namespace InsanityGaming.Store.Repository;

public class EquipmentRepository : BaseRepository<Equipment>, IEquipmentRepository
{
    public EquipmentRepository(ISqlSugarClient db) : base(db) { }

    public async Task<List<Equipment>> GetByPlayerAsync(ulong steamId) =>
        await Db.Queryable<Equipment>().Where(e => e.Id == steamId).ToListAsync();

    public async Task<Equipment?> GetByPlayerAndSlotAsync(ulong steamId, int slot) =>
        await Db.Queryable<Equipment>()
            .Where(e => e.Id == steamId && e.Slot == slot)
            .FirstAsync();
}
