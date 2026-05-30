namespace Muse.Workspace;

public interface IWorkspaceService
{
	WorkspaceState OpenWorkspace(string rootPath);

	WorkspaceTabState OpenDocument(string filePath);

	WorkspaceTabState? ActivateDocument(string documentId);

	WorkspaceTabState? MarkDirty(string documentId, bool isDirty = true);

	WorkspaceState GetState();
}
