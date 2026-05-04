using InsanityGaming.Store.Models;

namespace InsanityGaming.Store.Interfaces;

public interface IUserRepository : IRepository<Users>
{
    Task<Users?> GetByIdAsync(ulong id);
    Task<bool> UpdateCreditsAsync(ulong id, uint credits);
    Task<bool> BulkUpdateCreditsAsync(IReadOnlyList<(ulong SteamId, uint Credits)> updates);
}
