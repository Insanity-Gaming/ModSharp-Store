using InsanityGaming.Store.Interfaces;
using InsanityGaming.Store.Models;
using SqlSugar;

namespace InsanityGaming.Store.Repository;

public class EquipmentRepository : BaseRepository<Equipment>, IEquipmentRepository
{
    private readonly InterfaceBridge _bridge;

    public EquipmentRepository(ISqlSugarClient db, InterfaceBridge bridge) : base(db)
    {
        _bridge = bridge;
    }

    public async Task<List<Equipment>> GetByPlayerAsync(ulong steamId)
    {
        var serverId = _bridge.ServerAddress();
        return await Db.Queryable<Equipment>()
            .Where(e => e.Id == steamId && e.ServerId == serverId)
            .ToListAsync();
    }

    public async Task<Equipment?> GetByPlayerAndSlotAsync(ulong steamId, int slot)
    {
        var serverId = _bridge.ServerAddress();
        return await Db.Queryable<Equipment>()
            .Where(e => e.Id == steamId && e.ServerId == serverId && e.Slot == slot)
            .FirstAsync();
    }

    public async Task<List<Equipment>> GetByPlayerAndTeamAsync(ulong steamId, int team)
    {
        var serverId = _bridge.ServerAddress();
        return await Db.Queryable<Equipment>()
            .Where(e => e.Id == steamId && e.ServerId == serverId && e.Team == team)
            .ToListAsync();
    }

    public async Task<Equipment?> GetByPlayerAndSlotAndTeamAsync(ulong steamId, int slot, int team)
    {
        var serverId = _bridge.ServerAddress();
        return await Db.Queryable<Equipment>()
            .Where(e => e.Id == steamId && e.ServerId == serverId && e.Slot == slot && e.Team == team)
            .FirstAsync();
    }

    public async Task<bool> DeleteByPlayerSlotAndTeamAsync(ulong steamId, int slot, int team)
    {
        var serverId = _bridge.ServerAddress();
        return await Db.Deleteable<Equipment>()
            .Where(e => e.Id == steamId && e.ServerId == serverId && e.Slot == slot && e.Team == team)
            .ExecuteCommandAsync() > 0;
    }
}
