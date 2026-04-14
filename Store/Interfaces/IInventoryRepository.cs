using InsanityGaming.Store.Models;

namespace InsanityGaming.Store.Interfaces;

public interface IInventoryRepository : IRepository<Inventory>
{
    Task<List<Inventory>> GetByPlayerAsync(ulong steamId);
    Task<bool> DeleteByPlayerAsync(ulong steamId);
    Task<bool> DeleteByPlayerAndUniqueAsync(ulong steamId, string unique);
}
