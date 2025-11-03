using System.Data;
using MySqlConnector;

namespace Core.Db;

/// <summary>
/// 简单的 MySQL 管理工具类，封装常用的连接、查询、执行与事务方法
/// 依赖包：MySqlConnector
/// </summary>
public class MySqlManager
{
    private readonly string _connectionString;

    public MySqlManager(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    /// <summary>
    /// 构建连接字符串（推荐使用连接池）
    /// </summary>
    public static string BuildConnectionString(
        string host,
        int port,
        string user,
        string password,
        string database,
        bool pooling = true,
        int minPoolSize = 0,
        int maxPoolSize = 100,
        int timeoutSeconds = 15)
    {
        var csb = new MySqlConnectionStringBuilder
        {
            Server = host,
            Port = (uint)port,
            UserID = user,
            Password = password,
            Database = database,
            Pooling = pooling,
            MinimumPoolSize = (uint)minPoolSize,
            MaximumPoolSize = (uint)maxPoolSize,
            ConnectionTimeout = (uint)timeoutSeconds,
            AllowPublicKeyRetrieval = true,
            SslMode = MySqlSslMode.None // 根据实际需要调整
        };
        return csb.ConnectionString;
    }

    /// <summary>
    /// 打开并返回连接（调用方需 using 或 await using 释放）
    /// </summary>
    public async Task<MySqlConnection> OpenConnectionAsync(CancellationToken ct = default)
    {
        var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    /// <summary>
    /// 执行非查询 SQL（INSERT/UPDATE/DELETE）
    /// </summary>
    public async Task<int> ExecuteNonQueryAsync(string sql, IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = CreateCommand(conn, sql, parameters);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// 执行标量查询，返回首行首列并转换为指定类型
    /// </summary>
    public async Task<T?> ExecuteScalarAsync<T>(string sql, IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = CreateCommand(conn, sql, parameters);
        var obj = await cmd.ExecuteScalarAsync(ct);
        if (obj == null || obj is DBNull) return default;
        return (T)Convert.ChangeType(obj, typeof(T));
    }

    /// <summary>
    /// 执行查询，返回行字典列表（列名->值）
    /// </summary>
    public async Task<List<Dictionary<string, object?>>> QueryAsync(string sql, IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken ct = default)
    {
        var rows = new List<Dictionary<string, object?>>();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = CreateCommand(conn, sql, parameters);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var fieldCount = reader.FieldCount;
        while (await reader.ReadAsync(ct))
        {
            var dict = new Dictionary<string, object?>(fieldCount, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < fieldCount; i++)
            {
                var name = reader.GetName(i);
                var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                dict[name] = value;
            }
            rows.Add(dict);
        }
        return rows;
    }

    /// <summary>
    /// 执行事务包装的方法，action 内可执行多条语句
    /// </summary>
    public async Task<TResult> ExecuteTransactionAsync<TResult>(
        Func<MySqlConnection, MySqlTransaction, Task<TResult>> action,
        IsolationLevel isolation = IsolationLevel.ReadCommitted,
        CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(isolation, ct);
        try
        {
            var result = await action(conn, tx);
            await tx.CommitAsync(ct);
            return result;
        }
        catch
        {
            try { await tx.RollbackAsync(ct); } catch { /* ignore */ }
            throw;
        }
    }

    private static MySqlCommand CreateCommand(MySqlConnection conn, string sql, IReadOnlyDictionary<string, object?>? parameters)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        if (parameters != null)
        {
            foreach (var (name, value) in parameters)
            {
                cmd.Parameters.Add(new MySqlParameter(name, value ?? DBNull.Value));
            }
        }
        return cmd;
    }
}