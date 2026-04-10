using InsanityGaming.Store.Models;

namespace InsanityGaming.Store.Interfaces;

public interface IUserRepository : IRepository<Users>
{
    Task<Users?> GetByNameAsync(string name);
    Task<bool> UpdateCreditsAsync(ulong id, uint credits);
}
