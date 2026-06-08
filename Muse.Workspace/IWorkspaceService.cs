namespace Muse.Workspace;

public interface IWorkspaceService
{
	event EventHandler? WorkspaceChanged;

	WorkspaceState OpenWorkspace(string rootPath);

	OpenDocumentResult OpenDocument(string filePath);

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

	// Move an open tab to a new index within the open tabs list. Returns true if moved.
	bool MoveTab(string documentId, int newIndex);

	// --- File tree CRUD (S2-009) ---
	WorkspaceMutationResult CreateNode(string parentPath, string name, bool isDirectory);
	WorkspaceMutationResult RenameNode(string path, string newName);
	WorkspaceMutationResult RemoveNode(string path);
	WorkspaceMutationResult MoveNode(string sourcePath, string targetDirectoryPath);

	// --- Soft-flow atomic operations for open tabs (S2-009) ---
	WorkspaceMutationResult CloseAndRemove(string path);
	WorkspaceMutationResult CloseAndMove(string path, string targetDirectoryPath);

	// --- Session persistence (S2-009) ---
	WorkspaceSessionState? GetLastSession();
	void FlushSession();
	void InvalidateSession();

	// --- Recently closed (S2-009) ---
	IReadOnlyList<RecentlyClosedEntry> GetRecentlyClosed();
	void RemoveFromRecentlyClosed(string path);
}
