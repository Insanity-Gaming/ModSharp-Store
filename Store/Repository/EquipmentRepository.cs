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

    public async Task<List<Equipment>> GetByPlayerAndTeamAsync(ulong steamId, int team) =>
        await Db.Queryable<Equipment>()
            .Where(e => e.Id == steamId && e.Team == team)
            .ToListAsync();

    public async Task<Equipment?> GetByPlayerAndSlotAndTeamAsync(ulong steamId, int slot, int team) =>
        await Db.Queryable<Equipment>()
            .Where(e => e.Id == steamId && e.Slot == slot && e.Team == team)
            .FirstAsync();

    public async Task<bool> DeleteByPlayerSlotAndTeamAsync(ulong steamId, int slot, int team) =>
        await Db.Deleteable<Equipment>()
            .Where(e => e.Id == steamId && e.Slot == slot && e.Team == team)
            .ExecuteCommandAsync() > 0;
}
