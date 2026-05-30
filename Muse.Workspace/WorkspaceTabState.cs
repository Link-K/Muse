namespace Muse.Workspace;

public sealed record WorkspaceTabState(
	string DocumentId,
	string FilePath,
	bool IsDirty,
	DateTimeOffset LastTouchedAt)
{
	public bool HasExternalConflict { get; init; }

	public string? ConflictMessage { get; init; }
}
