namespace Muse.Workspace;

public sealed record WorkspaceRecoverySnapshot(
	string DocumentId,
	string FilePath,
	string Content,
	DateTimeOffset SavedAt);
