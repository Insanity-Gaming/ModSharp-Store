using InsanityGaming.Store.Interfaces;
using SqlSugar;

namespace InsanityGaming.Store.Repository;

public class UnitOfWork : IUnitOfWork
{
    private readonly ISqlSugarClient _db;

    public UnitOfWork(ISqlSugarClient db)
    {
        _db = db;
    }

    public async Task BeginTransactionAsync() => await _db.Ado.BeginTranAsync();
    public async Task CommitAsync() => await _db.Ado.CommitTranAsync();
    public async Task RollbackAsync() => await _db.Ado.RollbackTranAsync();
}
