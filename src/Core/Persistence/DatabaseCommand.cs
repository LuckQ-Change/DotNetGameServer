using System.Data;

namespace Core.Persistence;

/// <summary>与数据库厂商无关的命令描述。</summary>
public sealed record DatabaseCommand(
    string CommandText,
    IReadOnlyList<DatabaseParameter>? Parameters = null,
    CommandType CommandType = CommandType.Text,
    int? TimeoutSeconds = null);

/// <summary>
/// 命令参数。输出方向的 <see cref="Value"/> 会在命令完成后写回。
/// </summary>
public sealed class DatabaseParameter
{
    public DatabaseParameter(
        string name,
        object? value,
        DbType? dbType = null,
        ParameterDirection direction = ParameterDirection.Input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Value = value;
        DbType = dbType;
        Direction = direction;
    }

    public string Name { get; }

    public object? Value { get; set; }

    public DbType? DbType { get; }

    public ParameterDirection Direction { get; }
}
