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
    private readonly string _healthCheckCommand;
    private readonly int _healthCheckTimeoutSeconds;

    public AdoNetGameDatabase(
        DbProviderFactory providerFactory,
        string connectionString,
        int commandTimeoutSeconds = 30,
        int rollbackTimeoutSeconds = 5,
        string healthCheckCommand = "SELECT 1",
        int healthCheckTimeoutSeconds = 3)
    {
        _providerFactory = providerFactory;
        _connectionString = connectionString;
        _commandTimeoutSeconds = Math.Max(1, commandTimeoutSeconds);
        _rollbackTimeoutSeconds = Math.Max(1, rollbackTimeoutSeconds);
        _healthCheckCommand = healthCheckCommand ?? string.Empty;
        _healthCheckTimeoutSeconds = Math.Max(1, healthCheckTimeoutSeconds);
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
        if (string.IsNullOrWhiteSpace(_healthCheckCommand)) return;

        await using var command = connection.CreateCommand();
        command.CommandText = _healthCheckCommand;
        command.CommandType = CommandType.Text;
        command.CommandTimeout = _healthCheckTimeoutSeconds;
        await command.ExecuteScalarAsync(cancellationToken);
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
        var affected = await dbCommand.ExecuteNonQueryAsync(cancellationToken);
        CopyOutputParameters(dbCommand, command);
        return affected;
    }

    public async Task<T?> QuerySingleOrDefaultAsync<T>(
        DatabaseCommand command,
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        await using var dbCommand = CreateCommand(command);
        T? result;
        await using (var reader = await dbCommand.ExecuteReaderAsync(
            CommandBehavior.SingleResult | CommandBehavior.SingleRow,
            cancellationToken))
        {
            result = await reader.ReadAsync(cancellationToken) ? map(reader) : default;
        }

        CopyOutputParameters(dbCommand, command);
        return result;
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(
        DatabaseCommand command,
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        await using var dbCommand = CreateCommand(command);
        var result = new List<T>();
        await using (var reader = await dbCommand.ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                result.Add(map(reader));
        }

        CopyOutputParameters(dbCommand, command);
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

    private static void CopyOutputParameters(DbCommand dbCommand, DatabaseCommand command)
    {
        if (command.Parameters is null) return;
        foreach (DbParameter parameter in dbCommand.Parameters)
        {
            if (parameter.Direction == ParameterDirection.Input) continue;
            var match = command.Parameters.FirstOrDefault(item =>
                string.Equals(item.Name, parameter.ParameterName, StringComparison.Ordinal));
            if (match is null) continue;
            match.Value = parameter.Value is DBNull ? null : parameter.Value;
        }
    }
}
