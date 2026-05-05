using InsanityGaming.Store.Interfaces;
using SqlSugar;

namespace InsanityGaming.Store.Repository;

public class BaseRepository<T> : IRepository<T> where T : class, new()
{
    private readonly ISqlSugarClient _db;
    protected ISqlSugarClient Db => _db.CopyNew();

    public BaseRepository(ISqlSugarClient db)
    {
        _db = db;
    }

    public async Task<T?> GetByIdAsync(object id) =>
        await Db.Queryable<T>().InSingleAsync(id);

    public async Task<List<T>> GetAllAsync() =>
        await Db.Queryable<T>().ToListAsync();

    public async Task<bool> InsertAsync(T entity) =>
        await Db.Insertable(entity).ExecuteCommandAsync() > 0;

    public async Task<bool> UpdateAsync(T entity) =>
        await Db.Updateable(entity).ExecuteCommandAsync() > 0;

    public async Task<bool> DeleteAsync(object id) =>
        await Db.Deleteable<T>().In(id).ExecuteCommandAsync() > 0;

    public async Task<bool> UpsertAsync(T entity)
    {
        var storage = Db.Storageable(entity).ToStorage();
        await storage.AsInsertable.ExecuteCommandAsync();
        await storage.AsUpdateable.ExecuteCommandAsync();
        return true;
    }
}
