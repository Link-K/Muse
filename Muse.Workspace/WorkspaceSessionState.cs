namespace Muse.Workspace;

public sealed record WorkspaceSessionState(
    string WorkspaceRoot,
    IReadOnlyList<string> OpenTabIds,
    DateTimeOffset SavedAt);