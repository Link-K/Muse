using System.Security.Cryptography;
using System.Text.Json;

namespace Muse.Workspace;

public sealed class InMemoryWorkspaceService : IWorkspaceService
{
	private readonly IAutoSaveScheduler _autoSaveScheduler;
	private readonly Dictionary<string, string> _draftContents = new(StringComparer.Ordinal);
	private readonly bool _enableBackgroundAutoSave;
	private readonly TimeSpan _backgroundAutoSavePulse;
	private System.Threading.Timer? _autoSaveTimer;
	private WorkspaceState _state = new(null, [], [], null);

	public InMemoryWorkspaceService(IAutoSaveScheduler? autoSaveScheduler = null, bool enableBackgroundAutoSave = false, TimeSpan? backgroundAutoSavePulse = null)
	{
		_autoSaveScheduler = autoSaveScheduler ?? new MemoryAutoSaveScheduler();
		_enableBackgroundAutoSave = enableBackgroundAutoSave;
		_backgroundAutoSavePulse = backgroundAutoSavePulse ?? TimeSpan.FromSeconds(1);
		if (_enableBackgroundAutoSave)
		{
			_autoSaveTimer = new System.Threading.Timer(_ => FlushPendingAutoSaves(), null, _backgroundAutoSavePulse, _backgroundAutoSavePulse);
		}
	}

	public WorkspaceState OpenWorkspace(string rootPath)
	{
		var normalizedRoot = NormalizePath(rootPath);
		var recoveryTabs = LoadRecoveryTabs(normalizedRoot);
		IReadOnlyList<FileTreeNode> fileTree = Directory.Exists(normalizedRoot)
			? [BuildTree(normalizedRoot)]
			: Array.Empty<FileTreeNode>();

		_state = new WorkspaceState(
			normalizedRoot,
			fileTree,
			recoveryTabs,
			recoveryTabs.Count > 0 ? recoveryTabs[0].DocumentId : null);
		return _state;
	}

	public WorkspaceTabState OpenDocument(string filePath)
	{
		var normalizedPath = NormalizePath(filePath);
		var existing = _state.OpenTabs.FirstOrDefault(tab => tab.FilePath == normalizedPath);
		if (existing is not null)
		{
			return ActivateDocument(existing.DocumentId) ?? existing;
		}

		var tab = new WorkspaceTabState(
			DocumentId: normalizedPath,
			FilePath: normalizedPath,
			IsDirty: false,
			LastTouchedAt: DateTimeOffset.UtcNow);

		var tabs = _state.OpenTabs.Concat([tab]).ToArray();
		_state = _state with
		{
			OpenTabs = tabs,
			ActiveDocumentId = tab.DocumentId
		};

		return tab;
	}

	public WorkspaceTabState? ActivateDocument(string documentId)
	{
		if (string.IsNullOrWhiteSpace(documentId))
		{
			return null;
		}

		var normalizedId = NormalizePath(documentId);
		var index = IndexOfTab(normalizedId);
		if (index < 0)
		{
			return null;
		}

		var updated = _state.OpenTabs[index] with { LastTouchedAt = DateTimeOffset.UtcNow };
		var tabs = _state.OpenTabs.ToArray();
		tabs[index] = updated;

		_state = _state with
		{
			OpenTabs = tabs,
			ActiveDocumentId = updated.DocumentId
		};

		return updated;
	}

	public WorkspaceTabState? MarkDirty(string documentId, bool isDirty = true)
	{
		if (string.IsNullOrWhiteSpace(documentId))
		{
			return null;
		}

		var normalizedId = NormalizePath(documentId);
		var index = IndexOfTab(normalizedId);
		if (index < 0)
		{
			return null;
		}

		var updated = _state.OpenTabs[index] with
		{
			IsDirty = isDirty,
			LastTouchedAt = DateTimeOffset.UtcNow
		};

		var tabs = _state.OpenTabs.ToArray();
		tabs[index] = updated;
		_state = _state with { OpenTabs = tabs };

		if (updated.IsDirty)
		{
			_autoSaveScheduler.Schedule(updated.DocumentId);
		}

		return updated;
	}

	public WorkspaceTabState? UpdateDocumentDraft(string documentId, string content)
	{
		if (string.IsNullOrWhiteSpace(documentId) || content is null)
		{
			return null;
		}

		var normalizedId = NormalizePath(documentId);
		var index = IndexOfTab(normalizedId);
		if (index < 0)
		{
			return null;
		}

		_draftContents[normalizedId] = content;
		var updated = MarkDirty(normalizedId, true);
		if (updated is null)
		{
			return null;
		}

		_autoSaveScheduler.Schedule(normalizedId);
		return updated;
	}

	public string? GetDraftContent(string documentId)
	{
		if (string.IsNullOrWhiteSpace(documentId))
		{
			return null;
		}

		var normalizedId = NormalizePath(documentId);
		return _draftContents.TryGetValue(normalizedId, out var content) ? content : null;
	}

	public void FlushPendingAutoSaves()
	{
		var readyDocumentIds = _autoSaveScheduler.DrainReady(64);
		if (readyDocumentIds.Count == 0)
		{
			return;
		}

		foreach (var documentId in readyDocumentIds)
		{
			var content = GetDraftContent(documentId);
			if (content is null)
			{
				continue;
			}

			WriteRecoverySnapshot(documentId, content);
		}
	}

	public SaveDocumentResult SaveDocument(string documentId, string content)
	{
		if (string.IsNullOrWhiteSpace(documentId))
		{
			return SaveDocumentResult.Failure("invalid_document_id", "Document id is required.");
		}

		if (content is null)
		{
			return SaveDocumentResult.Failure("invalid_content", "Document content is required.");
		}

		var normalizedId = NormalizePath(documentId);
		var index = IndexOfTab(normalizedId);
		if (index < 0)
		{
			return SaveDocumentResult.Failure("document_not_found", "Document was not found in current workspace tabs.");
		}

		var targetPath = _state.OpenTabs[index].FilePath.Replace('/', Path.DirectorySeparatorChar);
		try
		{
			File.WriteAllText(targetPath, content);
			RemoveRecoverySnapshot(normalizedId);
			_draftContents[normalizedId] = content;
		}
		catch (Exception ex)
		{
			return SaveDocumentResult.Failure("io_error", $"Failed to write document: {ex.Message}");
		}

		var updated = _state.OpenTabs[index] with
		{
			IsDirty = false,
			LastTouchedAt = DateTimeOffset.UtcNow
		};

		var tabs = _state.OpenTabs.ToArray();
		tabs[index] = updated;
		_state = _state with { OpenTabs = tabs };

		return SaveDocumentResult.Success(updated);
	}

	public WorkspaceState GetState()
	{
		return _state;
	}

	private int IndexOfTab(string documentId)
	{
		for (var index = 0; index < _state.OpenTabs.Count; index++)
		{
			if (_state.OpenTabs[index].DocumentId == documentId)
			{
				return index;
			}
		}

		return -1;
	}

	private static FileTreeNode BuildTree(string path)
	{
		var children = new List<FileTreeNode>();
		foreach (var directory in Directory.EnumerateDirectories(path).OrderBy(static item => item, StringComparer.OrdinalIgnoreCase))
		{
			children.Add(BuildTree(directory));
		}

		foreach (var file in Directory.EnumerateFiles(path).OrderBy(static item => item, StringComparer.OrdinalIgnoreCase))
		{
			children.Add(new FileTreeNode(NormalizePath(file), System.IO.Path.GetFileName(file), false, []));
		}

		return new FileTreeNode(NormalizePath(path), System.IO.Path.GetFileName(path), true, children);
	}

	private static string NormalizePath(string path)
	{
		var fullPath = System.IO.Path.GetFullPath(path);
		return fullPath.Replace('\\', '/');
	}

	private IReadOnlyList<WorkspaceTabState> LoadRecoveryTabs(string normalizedRoot)
	{
		var recoveryDirectory = GetRecoveryDirectory(normalizedRoot);
		if (!Directory.Exists(recoveryDirectory))
		{
			return [];
		}

		var tabs = new List<WorkspaceTabState>();
		foreach (var file in Directory.EnumerateFiles(recoveryDirectory, "*.json").OrderBy(static item => item, StringComparer.OrdinalIgnoreCase))
		{
			try
			{
				var snapshot = JsonSerializer.Deserialize<WorkspaceRecoverySnapshot>(File.ReadAllText(file));
				if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.DocumentId))
				{
					continue;
				}

				_draftContents[snapshot.DocumentId] = snapshot.Content;
				tabs.Add(new WorkspaceTabState(snapshot.DocumentId, snapshot.FilePath, true, snapshot.SavedAt));
			}
			catch
			{
				// Ignore malformed recovery snapshots and continue loading the workspace.
			}
		}

		return tabs;
	}

	private void WriteRecoverySnapshot(string documentId, string content)
	{
		var recoveryDirectory = GetRecoveryDirectory(_state.WorkspaceRoot ?? string.Empty);
		Directory.CreateDirectory(recoveryDirectory);

		var normalizedId = NormalizePath(documentId);
		var tab = _state.OpenTabs.FirstOrDefault(item => item.DocumentId == normalizedId);
		if (tab is null)
		{
			return;
		}

		var snapshot = new WorkspaceRecoverySnapshot(normalizedId, tab.FilePath, content, DateTimeOffset.UtcNow);
		var snapshotPath = GetRecoverySnapshotPath(recoveryDirectory, normalizedId);
		File.WriteAllText(snapshotPath, JsonSerializer.Serialize(snapshot));
	}

	private void RemoveRecoverySnapshot(string documentId)
	{
		if (string.IsNullOrWhiteSpace(_state.WorkspaceRoot))
		{
			return;
		}

		var recoveryDirectory = GetRecoveryDirectory(_state.WorkspaceRoot);
		var snapshotPath = GetRecoverySnapshotPath(recoveryDirectory, NormalizePath(documentId));
		if (File.Exists(snapshotPath))
		{
			File.Delete(snapshotPath);
		}
	}

	private static string GetRecoveryDirectory(string normalizedRoot)
	{
		return Path.Combine(normalizedRoot.Replace('/', Path.DirectorySeparatorChar), ".muse", "recovery");
	}

	private static string GetRecoverySnapshotPath(string recoveryDirectory, string normalizedDocumentId)
	{
		var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalizedDocumentId)));
		return Path.Combine(recoveryDirectory, $"{hash}.json");
	}
}
