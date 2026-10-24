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

    public async Task<bool> BulkUpdateCreditsAsync(IReadOnlyList<(ulong SteamId, uint Credits)> updates)
    {
        if (updates.Count == 0) return true;

        // Db returns a fresh client on every access (CopyNew), so the transaction and every update
        // must go through this one instance; otherwise the updates run outside the transaction and
        // the client that began it is never committed or closed.
        var db = Db;

        var result = await db.Ado.UseTranAsync(async () =>
        {
            foreach (var (steamId, credits) in updates)
            {
                await db.Updateable<Users>()
                    .SetColumns(u => u.Credits == credits)
                    .Where(u => u.Id == steamId)
                    .ExecuteCommandAsync();
            }
        });

        return result.IsSuccess;
    }
}
