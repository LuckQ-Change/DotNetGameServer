using System.Data;
using System.Data.Common;

namespace Core.Persistence;

/// <summary>基于标准 ADO.NET DbProviderFactory 的数据库实现。</summary>
public sealed class AdoNetGameDatabase : IGameDatabase, IGameDatabaseHealthProbe
{
    private readonly DbProviderFactory _providerFactory;
    private readonly string _connectionString;
    private readonly int _commandTimeoutSeconds;
    private readonly int _rollbackTimeoutSeconds;

    public AdoNetGameDatabase(
        DbProviderFactory providerFactory,
        string connectionString,
        int commandTimeoutSeconds = 30,
        int rollbackTimeoutSeconds = 5)
    {
        _providerFactory = providerFactory;
        _connectionString = connectionString;
        _commandTimeoutSeconds = Math.Max(1, commandTimeoutSeconds);
        _rollbackTimeoutSeconds = Math.Max(1, rollbackTimeoutSeconds);
    }

    public async Task<int> ExecuteAsync(
        DatabaseCommand command,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var session = new AdoNetGameDatabaseSession(connection, null, _commandTimeoutSeconds);
        return await session.ExecuteAsync(command, cancellationToken);
    }

    public async Task<T?> QuerySingleOrDefaultAsync<T>(
        DatabaseCommand command,
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var session = new AdoNetGameDatabaseSession(connection, null, _commandTimeoutSeconds);
        return await session.QuerySingleOrDefaultAsync(command, map, cancellationToken);
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(
        DatabaseCommand command,
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var session = new AdoNetGameDatabaseSession(connection, null, _commandTimeoutSeconds);
        return await session.QueryAsync(command, map, cancellationToken);
    }

    public async Task<T> InTransactionAsync<T>(
        Func<IGameDatabaseSession, CancellationToken, Task<T>> action,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(isolationLevel, cancellationToken);
        var session = new AdoNetGameDatabaseSession(connection, transaction, _commandTimeoutSeconds);
        try
        {
            var result = await action(session, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            try
            {
                using var rollbackCts = new CancellationTokenSource(TimeSpan.FromSeconds(_rollbackTimeoutSeconds));
                await transaction.RollbackAsync(rollbackCts.Token);
            }
            catch
            {
                // 保留原始业务异常；连接释放仍会清理失败事务。
            }
            throw;
        }
    }

    public async Task CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
    }

    private DbConnection CreateConnection()
    {
        var connection = _providerFactory.CreateConnection()
            ?? throw new InvalidOperationException("数据库驱动未能创建 DbConnection。");
        connection.ConnectionString = _connectionString;
        return connection;
    }
}

internal sealed class AdoNetGameDatabaseSession(
    DbConnection connection,
    DbTransaction? transaction,
    int defaultCommandTimeoutSeconds) : IGameDatabaseSession
{
    public async Task<int> ExecuteAsync(
        DatabaseCommand command,
        CancellationToken cancellationToken = default)
    {
        await using var dbCommand = CreateCommand(command);
        return await dbCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<T?> QuerySingleOrDefaultAsync<T>(
        DatabaseCommand command,
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        await using var dbCommand = CreateCommand(command);
        await using var reader = await dbCommand.ExecuteReaderAsync(
            CommandBehavior.SingleResult | CommandBehavior.SingleRow,
            cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? map(reader) : default;
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(
        DatabaseCommand command,
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        await using var dbCommand = CreateCommand(command);
        await using var reader = await dbCommand.ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken);
        var result = new List<T>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(map(reader));
        return result;
    }

    private DbCommand CreateCommand(DatabaseCommand command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.CommandText);
        var dbCommand = connection.CreateCommand();
        dbCommand.CommandText = command.CommandText;
        dbCommand.CommandType = command.CommandType;
        dbCommand.CommandTimeout = Math.Max(1, command.TimeoutSeconds ?? defaultCommandTimeoutSeconds);
        dbCommand.Transaction = transaction;

        foreach (var item in command.Parameters ?? [])
        {
            var parameter = dbCommand.CreateParameter();
            parameter.ParameterName = item.Name;
            parameter.Value = item.Value ?? DBNull.Value;
            parameter.Direction = item.Direction;
            if (item.DbType.HasValue) parameter.DbType = item.DbType.Value;
            dbCommand.Parameters.Add(parameter);
        }

        return dbCommand;
    }
}
