namespace Muse.Workspace;

public interface IWorkspaceService
{
	event EventHandler? WorkspaceChanged;

	WorkspaceState OpenWorkspace(string rootPath);

	WorkspaceTabState OpenDocument(string filePath);

	bool CloseDocument(string documentId);

	WorkspaceTabState? ActivateDocument(string documentId);

	WorkspaceTabState? MarkDirty(string documentId, bool isDirty = true);

	WorkspaceTabState? UpdateDocumentDraft(string documentId, string content);

	string? GetDraftContent(string documentId);

	void FlushPendingAutoSaves();

	WorkspaceState RefreshWorkspaceFromDisk();

	IReadOnlyList<ConflictEvent> GetConflictEvents();

	SaveDocumentResult ResolveConflictBySavingLocal(string documentId, string localContent);

	SaveDocumentResult ResolveConflictByReloadingFromDisk(string documentId);

	SaveDocumentResult SaveDocument(string documentId, string content);

	WorkspaceState GetState();
}
