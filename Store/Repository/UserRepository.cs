using InsanityGaming.Store.Interfaces;
using InsanityGaming.Store.Models;
using SqlSugar;

namespace InsanityGaming.Store.Repository;

public class UserRepository : BaseRepository<Users>, IUserRepository
{
    public UserRepository(ISqlSugarClient db) : base(db) { }

    public async Task<Users?> GetByIdAsync(ulong id) =>
        await Db.Queryable<Users>().Where(u => u.Id == id).FirstAsync();

    public async Task<bool> UpdateCreditsAsync(ulong id, uint credits) =>
        await Db.Updateable<Users>()
            .SetColumns(u => u.Credits == credits)
            .Where(u => u.Id == id)
            .ExecuteCommandAsync() > 0;
}
