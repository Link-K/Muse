namespace Muse.Workspace;

public interface IWorkspaceService
{
	WorkspaceState OpenWorkspace(string rootPath);

	WorkspaceTabState OpenDocument(string filePath);

	WorkspaceTabState? ActivateDocument(string documentId);

	WorkspaceTabState? MarkDirty(string documentId, bool isDirty = true);

	WorkspaceTabState? UpdateDocumentDraft(string documentId, string content);

	string? GetDraftContent(string documentId);

	void FlushPendingAutoSaves();

	SaveDocumentResult SaveDocument(string documentId, string content);

	WorkspaceState GetState();
}
