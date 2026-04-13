using InsanityGaming.Store.Models;

namespace InsanityGaming.Store.Interfaces;

public interface IEquipmentRepository : IRepository<Equipment>
{
    Task<List<Equipment>> GetByPlayerAsync(ulong steamId);
    Task<Equipment?> GetByPlayerAndSlotAsync(ulong steamId, int slot);
    Task<List<Equipment>> GetByPlayerAndTeamAsync(ulong steamId, int team);
    Task<Equipment?> GetByPlayerAndSlotAndTeamAsync(ulong steamId, int slot, int team);
    Task<bool> DeleteByPlayerSlotAndTeamAsync(ulong steamId, int slot, int team);
}
