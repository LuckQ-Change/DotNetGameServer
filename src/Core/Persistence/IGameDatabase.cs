using System.Data;
using System.Data.Common;

namespace Core.Persistence;

/// <summary>
/// 游戏业务数据库入口。业务层只依赖该接口，可在外部接入 Npgsql、MySqlConnector、SqlClient
/// 或自定义实现；映射函数显式控制模型构造，不引入 ORM 约束。
/// </summary>
public interface IGameDatabase
{
    Task<int> ExecuteAsync(DatabaseCommand command, CancellationToken cancellationToken = default);

    Task<T?> QuerySingleOrDefaultAsync<T>(
        DatabaseCommand command,
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<T>> QueryAsync<T>(
        DatabaseCommand command,
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken = default);

    Task<T> InTransactionAsync<T>(
        Func<IGameDatabaseSession, CancellationToken, Task<T>> action,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default);
}

/// <summary>绑定到同一连接和事务的数据库会话。</summary>
public interface IGameDatabaseSession
{
    Task<int> ExecuteAsync(DatabaseCommand command, CancellationToken cancellationToken = default);

    Task<T?> QuerySingleOrDefaultAsync<T>(
        DatabaseCommand command,
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<T>> QueryAsync<T>(
        DatabaseCommand command,
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken = default);
}

/// <summary>供 readiness 使用的轻量连接探针，不执行厂商特定 SQL。</summary>
public interface IGameDatabaseHealthProbe
{
    Task CheckHealthAsync(CancellationToken cancellationToken = default);
}
