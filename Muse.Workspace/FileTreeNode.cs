namespace Muse.Workspace;

public sealed record FileTreeNode(
	string Path,
	string Name,
	bool IsDirectory,
	IReadOnlyList<FileTreeNode> Children);
