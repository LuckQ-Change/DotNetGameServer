using BasicServer.Protocol;

using Core;
using Core.Persistence;

namespace BasicServer;

public sealed class SaveNoteHandler(IGameDatabase database) : IMessageHandler<SaveNoteRequest, SaveNoteResponse>
{
    public async Task<SaveNoteResponse?> HandleAsync(SaveNoteRequest request, IServerContext context)
    {
        if (!TryValidateNoteId(request.NoteId, out var error))
            return new SaveNoteResponse { Success = false, Code = 400, Message = error };

        await database.ExecuteAsync(
            new DatabaseCommand(
                """
                INSERT INTO notes (note_id, body) VALUES (@note_id, @body)
                ON CONFLICT(note_id) DO UPDATE SET body = excluded.body;
                """,
                [
                    new DatabaseParameter("@note_id", request.NoteId),
                    new DatabaseParameter("@body", request.Body ?? string.Empty)
                ]),
            context.CancellationToken);
        return new SaveNoteResponse { Success = true, Code = 0, Message = "saved" };
    }

    internal static bool TryValidateNoteId(string? noteId, out string error)
    {
        if (string.IsNullOrWhiteSpace(noteId) || noteId.Length > 128)
        {
            error = "invalid_note_id";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

public sealed class LoadNoteHandler(IGameDatabase database) : IMessageHandler<LoadNoteRequest, LoadNoteResponse>
{
    public async Task<LoadNoteResponse?> HandleAsync(LoadNoteRequest request, IServerContext context)
    {
        if (!SaveNoteHandler.TryValidateNoteId(request.NoteId, out var error))
            return new LoadNoteResponse { Success = false, Code = 400, Message = error };

        var body = await database.QuerySingleOrDefaultAsync(
            new DatabaseCommand(
                "SELECT body FROM notes WHERE note_id = @note_id",
                [new DatabaseParameter("@note_id", request.NoteId)]),
            reader => reader.GetString(0),
            context.CancellationToken);
        return body is null
            ? new LoadNoteResponse { Success = false, Code = 404, Message = "note_not_found" }
            : new LoadNoteResponse { Success = true, Code = 0, Message = "ok", Body = body };
    }
}
