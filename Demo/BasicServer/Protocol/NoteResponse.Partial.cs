using Shared;

namespace BasicServer.Protocol;

public sealed partial class SaveNoteResponse : IServerResponse;

public sealed partial class LoadNoteResponse : IServerResponse;

public sealed partial class GuestLoginResponse : IServerResponse;

public sealed partial class RefreshSessionResponse : IServerResponse;
