using Core;
using Core.Persistence;

namespace BasicServer;

/// <summary>在服务器启动时确保笔记表存在。</summary>
public sealed class NoteSchemaComponent(IGameDatabase database) : IServerComponent
{
    public string Name => nameof(NoteSchemaComponent);

    public Task InitializeAsync() =>
        database.ExecuteAsync(new DatabaseCommand(
            """
            CREATE TABLE IF NOT EXISTS notes (
                note_id TEXT PRIMARY KEY NOT NULL,
                body TEXT NOT NULL
            );
            """));

    public Task DestroyAsync() => Task.CompletedTask;
}
