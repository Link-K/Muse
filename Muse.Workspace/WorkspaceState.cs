namespace Muse.Workspace;

public sealed record WorkspaceState(
	string? WorkspaceRoot,
	IReadOnlyList<FileTreeNode> FileTree,
	IReadOnlyList<WorkspaceTabState> OpenTabs,
	string? ActiveDocumentId);
