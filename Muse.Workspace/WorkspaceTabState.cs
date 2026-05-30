namespace Muse.Workspace;

public sealed record WorkspaceTabState(
	string DocumentId,
	string FilePath,
	bool IsDirty,
	DateTimeOffset LastTouchedAt);
