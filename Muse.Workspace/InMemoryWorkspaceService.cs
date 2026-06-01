using System.Security.Cryptography;
using System.Text.Json;

namespace Muse.Workspace;

public sealed class InMemoryWorkspaceService : IWorkspaceService
{
	private const string InternalWorkspaceFolderName = ".muse";
	private readonly IAutoSaveScheduler _autoSaveScheduler;
	private readonly Dictionary<string, string> _draftContents = new(StringComparer.Ordinal);
	private readonly List<ConflictEvent> _conflictEvents = [];
	private readonly bool _enableBackgroundAutoSave;
	private readonly TimeSpan _backgroundAutoSavePulse;
	private readonly TimeSpan _workspaceRefreshDebounce = TimeSpan.FromMilliseconds(200);
	private readonly object _workspaceRefreshGate = new();
	private System.Threading.Timer? _autoSaveTimer;
	private System.Threading.Timer? _workspaceRefreshTimer;
	private FileSystemWatcher? _workspaceWatcher;
	private WorkspaceState _state = new(null, [], [], null);

	public event EventHandler? WorkspaceChanged;

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
		DisposeWorkspaceWatcher();
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
		SetupWorkspaceWatcher(normalizedRoot);
		RaiseWorkspaceChanged();
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
		RaiseWorkspaceChanged();

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
		RaiseWorkspaceChanged();

		return updated;
	}

	public bool CloseDocument(string documentId)
	{
		if (string.IsNullOrWhiteSpace(documentId)) return false;
		var normalizedId = NormalizePath(documentId);
		var tabs = _state.OpenTabs.ToList();
		var index = tabs.FindIndex(t => t.DocumentId == normalizedId);
		if (index < 0) return false;
		tabs.RemoveAt(index);
		string? newActive = tabs.Count > 0 ? tabs[Math.Max(0, index - 1)].DocumentId : null;
		_state = _state with { OpenTabs = tabs, ActiveDocumentId = newActive };
		RaiseWorkspaceChanged();
		return true;
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

		RaiseWorkspaceChanged();

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
		RaiseWorkspaceChanged();
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

		RaiseWorkspaceChanged();
	}

	public WorkspaceState RefreshWorkspaceFromDisk()
	{
		var root = _state.WorkspaceRoot;
		if (string.IsNullOrWhiteSpace(root))
		{
			return _state;
		}

		var normalizedRoot = NormalizePath(root);
		IReadOnlyList<FileTreeNode> fileTree = Directory.Exists(normalizedRoot)
			? [BuildTree(normalizedRoot)]
			: Array.Empty<FileTreeNode>();

		var updatedTabs = new List<WorkspaceTabState>();
		foreach (var tab in _state.OpenTabs)
		{
			var filePath = tab.FilePath.Replace('/', Path.DirectorySeparatorChar);
			if (File.Exists(filePath) && !tab.IsDirty)
			{
				_draftContents[tab.DocumentId] = File.ReadAllText(filePath);
				updatedTabs.Add(tab with
				{
					LastTouchedAt = DateTimeOffset.UtcNow,
					HasExternalConflict = false,
					ConflictMessage = null
				});
				continue;
			}

			if (File.Exists(filePath) && tab.IsDirty)
			{
				var diskContent = File.ReadAllText(filePath);
				var draftContent = GetDraftContent(tab.DocumentId);
				var hasConflict = draftContent is not null && diskContent != draftContent;
				if (hasConflict && !tab.HasExternalConflict)
				{
					AppendConflictEvent(tab.DocumentId, "detected_external_change", "检测到外部文件变更，当前草稿尚未同步。");
				}
				updatedTabs.Add(tab with
				{
					LastTouchedAt = DateTimeOffset.UtcNow,
					HasExternalConflict = hasConflict,
					ConflictMessage = hasConflict ? "检测到外部文件变更，当前草稿尚未同步。" : null
				});
				continue;
			}

			if (!File.Exists(filePath) && tab.IsDirty)
			{
				if (!tab.HasExternalConflict)
				{
					AppendConflictEvent(tab.DocumentId, "detected_external_delete", "检测到外部文件被删除，当前草稿仍保留在本地。");
				}
				updatedTabs.Add(tab with
				{
					LastTouchedAt = DateTimeOffset.UtcNow,
					HasExternalConflict = true,
					ConflictMessage = "检测到外部文件被删除，当前草稿仍保留在本地。"
				});
				continue;
			}

			updatedTabs.Add(tab);
		}

		_state = _state with
		{
			FileTree = fileTree,
			OpenTabs = updatedTabs
		};

		RaiseWorkspaceChanged();
		return _state;
	}

	public IReadOnlyList<ConflictEvent> GetConflictEvents()
	{
		return _conflictEvents.ToArray();
	}

	public SaveDocumentResult ResolveConflictBySavingLocal(string documentId, string localContent)
	{
		if (string.IsNullOrWhiteSpace(documentId))
		{
			return SaveDocumentResult.Failure("invalid_document_id", "Document id is required.");
		}

		if (localContent is null)
		{
			return SaveDocumentResult.Failure("invalid_content", "Local content is required.");
		}

		var result = SaveDocument(documentId, localContent);
		if (result.Succeeded)
		{
			AppendConflictEvent(NormalizePath(documentId), "resolved_save_local", "已保留本地并覆盖保存外部文件。");
		}
		else
		{
			AppendConflictEvent(NormalizePath(documentId), "resolve_failed", $"保留本地并覆盖保存失败：{result.Message}");
		}

		return result;
	}

	public SaveDocumentResult ResolveConflictByReloadingFromDisk(string documentId)
	{
		if (string.IsNullOrWhiteSpace(documentId))
		{
			var failed = SaveDocumentResult.Failure("invalid_document_id", "Document id is required.");
			AppendConflictEvent("", "resolve_failed", $"重载外部内容失败：{failed.Message}");
			return failed;
		}

		var normalizedId = NormalizePath(documentId);
		var index = IndexOfTab(normalizedId);
		if (index < 0)
		{
			var failed = SaveDocumentResult.Failure("document_not_found", "Document was not found in current workspace tabs.");
			AppendConflictEvent(normalizedId, "resolve_failed", $"重载外部内容失败：{failed.Message}");
			return failed;
		}

		var filePath = _state.OpenTabs[index].FilePath.Replace('/', Path.DirectorySeparatorChar);
		if (!File.Exists(filePath))
		{
			var failed = SaveDocumentResult.Failure("external_missing", "External file no longer exists.");
			AppendConflictEvent(normalizedId, "resolve_failed", $"重载外部内容失败：{failed.Message}");
			return failed;
		}

		string diskContent;
		try
		{
			diskContent = File.ReadAllText(filePath);
		}
		catch (Exception ex)
		{
			var failed = SaveDocumentResult.Failure("io_error", $"Failed to read external file: {ex.Message}");
			AppendConflictEvent(normalizedId, "resolve_failed", $"重载外部内容失败：{failed.Message}");
			return failed;
		}

		_draftContents[normalizedId] = diskContent;
		RemoveRecoverySnapshot(normalizedId);

		var updated = _state.OpenTabs[index] with
		{
			IsDirty = false,
			LastTouchedAt = DateTimeOffset.UtcNow,
			HasExternalConflict = false,
			ConflictMessage = null
		};

		var tabs = _state.OpenTabs.ToArray();
		tabs[index] = updated;
		_state = _state with { OpenTabs = tabs };

		AppendConflictEvent(normalizedId, "resolved_reload_external", "已丢弃本地并重载外部文件内容。");
		RaiseWorkspaceChanged();
		return new SaveDocumentResult(true, "reloaded", "Reloaded external content.", updated);
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
			LastTouchedAt = DateTimeOffset.UtcNow,
			HasExternalConflict = false,
			ConflictMessage = null
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
			if (IsInternalWorkspacePath(directory))
			{
				continue;
			}

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

	private static bool IsInternalWorkspacePath(string path)
	{
		return path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
			.Any(segment => string.Equals(segment, InternalWorkspaceFolderName, StringComparison.OrdinalIgnoreCase));
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

	private void SetupWorkspaceWatcher(string normalizedRoot)
	{
		if (!Directory.Exists(normalizedRoot))
		{
			return;
		}

		_workspaceWatcher = new FileSystemWatcher(normalizedRoot)
		{
			IncludeSubdirectories = true,
			NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
			EnableRaisingEvents = true
		};

		_workspaceWatcher.Changed += HandleWorkspaceFileEvent;
		_workspaceWatcher.Created += HandleWorkspaceFileEvent;
		_workspaceWatcher.Deleted += HandleWorkspaceFileEvent;
		_workspaceWatcher.Renamed += HandleWorkspaceRenamedEvent;
	}

	private void HandleWorkspaceFileEvent(object sender, FileSystemEventArgs e)
	{
		if (IsInternalWorkspacePath(e.FullPath))
		{
			return;
		}

		RequestWorkspaceRefresh();
	}

	private void HandleWorkspaceRenamedEvent(object sender, RenamedEventArgs e)
	{
		if (IsInternalWorkspacePath(e.FullPath) || IsInternalWorkspacePath(e.OldFullPath))
		{
			return;
		}

		RequestWorkspaceRefresh();
	}

	private void RequestWorkspaceRefresh()
	{
		lock (_workspaceRefreshGate)
		{
			_workspaceRefreshTimer ??= new System.Threading.Timer(_ => RefreshWorkspaceFromDisk(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
			_workspaceRefreshTimer.Change(_workspaceRefreshDebounce, Timeout.InfiniteTimeSpan);
		}
	}

	private void DisposeWorkspaceWatcher()
	{
		if (_workspaceWatcher is null)
		{
			return;
		}

		_workspaceWatcher.EnableRaisingEvents = false;
		_workspaceWatcher.Changed -= HandleWorkspaceFileEvent;
		_workspaceWatcher.Created -= HandleWorkspaceFileEvent;
		_workspaceWatcher.Deleted -= HandleWorkspaceFileEvent;
		_workspaceWatcher.Renamed -= HandleWorkspaceRenamedEvent;
		_workspaceWatcher.Dispose();
		_workspaceWatcher = null;
	}

	private void RaiseWorkspaceChanged()
	{
		WorkspaceChanged?.Invoke(this, EventArgs.Empty);
	}

	private void AppendConflictEvent(string documentId, string action, string message)
	{
		_conflictEvents.Add(new ConflictEvent(documentId, action, message, DateTimeOffset.UtcNow));
		if (_conflictEvents.Count > 100)
		{
			_conflictEvents.RemoveRange(0, _conflictEvents.Count - 100);
		}
	}
}
