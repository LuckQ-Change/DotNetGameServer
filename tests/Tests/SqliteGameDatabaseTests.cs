using System.Data.Common;

using Core.Persistence;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Tests;

public sealed class SqliteGameDatabaseTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"dngs-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task HealthQueryAndNoteRoundTrip_UseSqlite()
    {
        EnsureSqlite();
        var database = CreateDatabase();
        await database.CheckHealthAsync();
        await database.ExecuteAsync(new DatabaseCommand(
            """
            CREATE TABLE notes (
                note_id TEXT PRIMARY KEY NOT NULL,
                body TEXT NOT NULL
            );
            """));

        await database.ExecuteAsync(new DatabaseCommand(
            "INSERT INTO notes (note_id, body) VALUES (@note_id, @body)",
            [
                new DatabaseParameter("@note_id", "alpha"),
                new DatabaseParameter("@body", "hello")
            ]));

        var body = await database.QuerySingleOrDefaultAsync(
            new DatabaseCommand(
                "SELECT body FROM notes WHERE note_id = @note_id",
                [new DatabaseParameter("@note_id", "alpha")]),
            reader => reader.GetString(0));

        Assert.Equal("hello", body);
    }

    [Fact]
    public async Task TransactionRollback_DoesNotKeepInsertedRow()
    {
        EnsureSqlite();
        var database = CreateDatabase();
        await database.ExecuteAsync(new DatabaseCommand(
            """
            CREATE TABLE notes (
                note_id TEXT PRIMARY KEY NOT NULL,
                body TEXT NOT NULL
            );
            """));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.InTransactionAsync<int>(async (session, cancellationToken) =>
            {
                await session.ExecuteAsync(
                    new DatabaseCommand(
                        "INSERT INTO notes (note_id, body) VALUES (@note_id, @body)",
                        [
                            new DatabaseParameter("@note_id", "beta"),
                            new DatabaseParameter("@body", "rolled-back")
                        ]),
                    cancellationToken);
                throw new InvalidOperationException("business failure");
            }));

        var count = await database.QuerySingleOrDefaultAsync(
            new DatabaseCommand("SELECT COUNT(*) FROM notes"),
            reader => reader.GetInt32(0));
        Assert.Equal(0, count);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        TryDelete(_path);
        TryDelete(_path + "-wal");
        TryDelete(_path + "-shm");
    }

    private static void EnsureSqlite()
    {
        if (!DbProviderFactories.TryGetFactory("Microsoft.Data.Sqlite", out _))
            DbProviderFactories.RegisterFactory("Microsoft.Data.Sqlite", SqliteFactory.Instance);
    }

    private AdoNetGameDatabase CreateDatabase() =>
        new(SqliteFactory.Instance, $"Data Source={_path}");

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // 连接池刚释放时文件可能仍被占用，临时目录会随后清理。
        }
    }
}
