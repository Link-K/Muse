namespace Muse.Workspace;

public sealed record WorkspaceTabState(
	string DocumentId,
	string FilePath,
	bool IsDirty,
	DateTimeOffset LastTouchedAt)
{
	public string FileName => System.IO.Path.GetFileName(FilePath);

	public bool HasExternalConflict { get; init; }

	public string? ConflictMessage { get; init; }

	// S2-009: session-based recovery indicators
	public bool HasUnsavedRecovery { get; init; }

	public bool IsMissingOnDisk { get; init; }
}
