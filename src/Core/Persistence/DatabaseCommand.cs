using System.Data;

namespace Core.Persistence;

/// <summary>与数据库厂商无关的命令描述。</summary>
public sealed record DatabaseCommand(
    string CommandText,
    IReadOnlyList<DatabaseParameter>? Parameters = null,
    CommandType CommandType = CommandType.Text,
    int? TimeoutSeconds = null);

public sealed record DatabaseParameter(
    string Name,
    object? Value,
    DbType? DbType = null,
    ParameterDirection Direction = ParameterDirection.Input);
