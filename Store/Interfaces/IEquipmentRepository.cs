using InsanityGaming.Store.Models;

namespace InsanityGaming.Store.Interfaces;

public interface IEquipmentRepository : IRepository<Equipment>
{
    Task<List<Equipment>> GetByPlayerAsync(ulong steamId);
    Task<Equipment?> GetByPlayerAndSlotAsync(ulong steamId, int slot);
}
