using System.Security.Cryptography;
using System.Text.Json;

namespace Muse.Workspace;

public sealed class InMemoryWorkspaceService : IWorkspaceService
{
	private const string InternalWorkspaceFolderName = ".muse";
	private const int MaxRecentlyClosedEntries = 20;
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = false,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};
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

		// 1. Load session (session.json) for normal exit recovery
		var session = LoadSession(normalizedRoot);

		// 2. Load recovery (recovery/*.json) for crash recovery drafts
		var recoveryTabs = LoadRecoveryTabs(normalizedRoot);

		// 3. Load persisted tab order (tabs.json) for ordering
		var persistedOrder = LoadPersistedTabOrder(normalizedRoot);

		// 4. Build tabs: session-first
		var tabs = new List<WorkspaceTabState>();
		if (session is not null)
		{
			foreach (var id in session.OpenTabIds)
			{
				var normalizedId = NormalizePath(id);
				var filePath = normalizedId.Replace('/', Path.DirectorySeparatorChar);
				if (File.Exists(filePath))
				{
					var hasRecovery = recoveryTabs.Any(r => r.DocumentId == normalizedId);
					tabs.Add(new WorkspaceTabState(normalizedId, id, hasRecovery, DateTimeOffset.UtcNow)
					{
						HasUnsavedRecovery = hasRecovery
					});
				}
				else
				{
					// missing on disk — gray out
					tabs.Add(new WorkspaceTabState(normalizedId, id, false, DateTimeOffset.UtcNow)
					{
						IsMissingOnDisk = true
					});
					// add to recently-closed if first discovery
					AddToRecentlyClosed(normalizedId, Path.GetFileName(normalizedId.Replace('/', Path.DirectorySeparatorChar)), null);
				}
			}
		}
		else
		{
			// No session — use recovery tabs directly (existing behavior)
			tabs = recoveryTabs.Select(r =>
				new WorkspaceTabState(r.DocumentId, r.FilePath, true, r.LastTouchedAt) { HasUnsavedRecovery = true }
			).Cast<WorkspaceTabState>().ToList();
		}

		// 5. Apply persisted tab order if available
		if (persistedOrder?.Count > 0)
		{
			if (tabs.Count > 0)
			{
				var dict = tabs.ToDictionary(t => t.DocumentId, t => t, StringComparer.Ordinal);
				var ordered = new List<WorkspaceTabState>();
				foreach (var id in persistedOrder)
					if (dict.TryGetValue(id, out var t)) ordered.Add(t);
				foreach (var t in tabs)
					if (!ordered.Any(x => x.DocumentId == t.DocumentId)) ordered.Add(t);
				tabs = ordered;
			}
			else
			{
				// No session or recovery but persisted order exists — construct from persisted ids
				var constructed = new List<WorkspaceTabState>();
				foreach (var id in persistedOrder)
				{
					if (string.IsNullOrWhiteSpace(id)) continue;
					try
					{
						var filePath = id.Replace('/', Path.DirectorySeparatorChar);
						if (File.Exists(filePath))
							constructed.Add(new WorkspaceTabState(id, id, false, DateTimeOffset.UtcNow));
					}
					catch { }
				}
				if (constructed.Count > 0) tabs = constructed;
			}
		}

		// 6. Build file tree
		IReadOnlyList<FileTreeNode> fileTree = Directory.Exists(normalizedRoot)
			? [BuildTree(normalizedRoot)]
			: Array.Empty<FileTreeNode>();

		_state = new WorkspaceState(normalizedRoot, fileTree, tabs, tabs.Count > 0 ? tabs[0].DocumentId : null);
		SetupWorkspaceWatcher(normalizedRoot);
		RaiseWorkspaceChanged();
		return _state;
	}

	public OpenDocumentResult OpenDocument(string filePath)
	{
		var normalizedPath = NormalizePath(filePath);

		// 后备保护：在服务层进行轻量文件格式检查，避免外部调用直接加载不支持的文件导致 UI 卡死
		if (!IsFileSupported(normalizedPath, out var reason))
		{
			return OpenDocumentResult.Failure(reason);
		}
		var existing = _state.OpenTabs.FirstOrDefault(tab => tab.FilePath == normalizedPath);
		if (existing is not null)
		{
			var activated = ActivateDocument(existing.DocumentId) ?? existing;
			return OpenDocumentResult.Success(activated);
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

		return OpenDocumentResult.Success(tab);
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
		var closedTab = tabs[index];
		tabs.RemoveAt(index);
		string? newActive = tabs.Count > 0 ? tabs[Math.Max(0, index - 1)].DocumentId : null;
		_state = _state with { OpenTabs = tabs, ActiveDocumentId = newActive };
		long? sizeBytes = null;
		try
		{
			var filePath = closedTab.FilePath.Replace('/', Path.DirectorySeparatorChar);
			if (File.Exists(filePath))
			{
				sizeBytes = new FileInfo(filePath).Length;
			}
		}
		catch
		{
			// ignore size lookup failures
		}
		AddToRecentlyClosed(normalizedId, closedTab.FileName, sizeBytes);
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

	public bool MoveTab(string documentId, int newIndex)
	{
		if (string.IsNullOrWhiteSpace(documentId)) return false;
		var normalizedId = NormalizePath(documentId);
		var tabs = _state.OpenTabs.ToList();
		var oldIndex = tabs.FindIndex(t => t.DocumentId == normalizedId);
		if (oldIndex < 0) return false;
		if (newIndex < 0) newIndex = 0;
		if (newIndex >= tabs.Count) newIndex = tabs.Count - 1;
		if (oldIndex == newIndex) return true;

		var item = tabs[oldIndex];
		tabs.RemoveAt(oldIndex);
		// adjust target index if removal was before insertion point
		if (oldIndex < newIndex) newIndex--;
		tabs.Insert(newIndex, item);
		_state = _state with { OpenTabs = tabs };
		// Persist the updated tab order for this workspace
		try
		{
			SavePersistedTabOrder(_state.WorkspaceRoot, tabs.Select(t => t.DocumentId).ToArray());
		}
		catch
		{
			// ignore persistence failures to keep service resilient
		}
		RaiseWorkspaceChanged();
		return true;
	}

	private static string GetSettingsDirectory(string normalizedRoot)
	{
		return Path.Combine(normalizedRoot.Replace('/', Path.DirectorySeparatorChar), ".muse", "settings");
	}

	private static string GetPersistedTabsPath(string normalizedRoot)
	{
		return Path.Combine(GetSettingsDirectory(normalizedRoot), "tabs.json");
	}

	private void SavePersistedTabOrder(string? normalizedRoot, string[] documentIds)
	{
		if (string.IsNullOrWhiteSpace(normalizedRoot)) return;
		var settingsDir = GetSettingsDirectory(normalizedRoot);
		Directory.CreateDirectory(settingsDir);
		var path = GetPersistedTabsPath(normalizedRoot);
		File.WriteAllText(path, JsonSerializer.Serialize(documentIds));
	}

	private List<string>? LoadPersistedTabOrder(string normalizedRoot)
	{
		try
		{
			var path = GetPersistedTabsPath(normalizedRoot);
			if (!File.Exists(path)) return null;
			var json = File.ReadAllText(path);
			var arr = JsonSerializer.Deserialize<string[]>(json);
			return arr is null ? null : new List<string>(arr);
		}
		catch
		{
			return null;
		}
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

	// --- S2-009: File tree CRUD ---

		public WorkspaceMutationResult CreateNode(string parentPath, string name, bool isDirectory)
		{
			var validation = ValidateNodeName(name);
			if (validation is not null) return validation;

			if (string.IsNullOrWhiteSpace(_state.WorkspaceRoot))
				return WorkspaceMutationResult.Failure("outside_workspace", "No workspace is open.");

			var normalizedParent = NormalizePath(parentPath);
			if (!IsUnderWorkspaceRoot(normalizedParent, out var outside) || outside)
				return WorkspaceMutationResult.Failure("outside_workspace", "Parent path is outside the workspace.");

			if (IsInsideDotMuseFolder(normalizedParent))
				return WorkspaceMutationResult.Failure("forbidden_path", "Cannot create nodes inside the .muse workspace folder.");

			var targetPath = Path.Combine(normalizedParent.Replace('/', Path.DirectorySeparatorChar), name);
			var normalizedTarget = NormalizePath(targetPath);

			if (!IsUnderWorkspaceRoot(normalizedTarget, out _))
				return WorkspaceMutationResult.Failure("outside_workspace", "Target path is outside the workspace.");

			try
			{
				if (Directory.Exists(targetPath) || File.Exists(targetPath))
					return WorkspaceMutationResult.Failure("path_conflict", "A file or directory already exists at the target path.");

				if (isDirectory)
				{
					Directory.CreateDirectory(targetPath);
				}
				else
				{
					var dir = Path.GetDirectoryName(targetPath);
					if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
						Directory.CreateDirectory(dir);
					File.WriteAllText(targetPath, string.Empty);
				}

				RefreshWorkspaceFromDisk();
				return WorkspaceMutationResult.Success(isDirectory ? "created_directory" : "created", normalizedTarget);
			}
			catch (Exception ex)
			{
				return WorkspaceMutationResult.Failure("io_error", $"Failed to create node: {ex.Message}");
			}
		}

		public WorkspaceMutationResult RenameNode(string path, string newName)
		{
			var validation = ValidateNodeName(newName);
			if (validation is not null) return validation;

			if (string.IsNullOrWhiteSpace(_state.WorkspaceRoot))
				return WorkspaceMutationResult.Failure("outside_workspace", "No workspace is open.");

			var normalizedPath = NormalizePath(path);
			if (!IsUnderWorkspaceRoot(normalizedPath, out var outside) || outside)
				return WorkspaceMutationResult.Failure("outside_workspace", "Source path is outside the workspace.");

			var sourceDir = Path.GetDirectoryName(normalizedPath.Replace('/', Path.DirectorySeparatorChar)) ?? string.Empty;
			var targetPath = Path.Combine(sourceDir, newName);
			var normalizedTarget = NormalizePath(targetPath);

			if (!IsUnderWorkspaceRoot(normalizedTarget, out _))
				return WorkspaceMutationResult.Failure("outside_workspace", "Target path is outside the workspace.");

			try
			{
				if (Directory.Exists(targetPath) || File.Exists(targetPath))
					return WorkspaceMutationResult.Failure("path_conflict", "A file or directory already exists with the new name.");

				if (Directory.Exists(normalizedPath.Replace('/', Path.DirectorySeparatorChar)))
				{
					Directory.Move(normalizedPath.Replace('/', Path.DirectorySeparatorChar), targetPath);
				}
				else if (File.Exists(normalizedPath.Replace('/', Path.DirectorySeparatorChar)))
				{
					File.Move(normalizedPath.Replace('/', Path.DirectorySeparatorChar), targetPath);
				}
				else
				{
					return WorkspaceMutationResult.Failure("not_found", "Source path does not exist.");
				}

				UpdateTabPaths(normalizedPath, normalizedTarget);
				UpdateDraftKeys(normalizedPath, normalizedTarget);
				UpdateRecoverySnapshots(normalizedPath, normalizedTarget);

				RefreshWorkspaceFromDisk();
				return WorkspaceMutationResult.Success("renamed", normalizedTarget);
			}
			catch (Exception ex)
			{
				return WorkspaceMutationResult.Failure("io_error", $"Failed to rename node: {ex.Message}");
			}
		}

		public WorkspaceMutationResult RemoveNode(string path)
		{
			if (string.IsNullOrWhiteSpace(_state.WorkspaceRoot))
				return WorkspaceMutationResult.Failure("outside_workspace", "No workspace is open.");

			var normalizedPath = NormalizePath(path);
			if (!IsUnderWorkspaceRoot(normalizedPath, out var outside) || outside)
				return WorkspaceMutationResult.Failure("outside_workspace", "Path is outside the workspace.");

			var fileSystemPath = normalizedPath.Replace('/', Path.DirectorySeparatorChar);

			foreach (var tab in _state.OpenTabs)
			{
				if (tab.FilePath.StartsWith(normalizedPath, StringComparison.Ordinal))
				{
					if (tab.IsDirty)
						return WorkspaceMutationResult.Failure("open_tab_unsaved", "The file or directory is open in a tab with unsaved changes.");
					return WorkspaceMutationResult.Failure("open_tab_unsaved", "The file or directory is currently open. Use CloseAndRemove to close it first.");
				}
			}

			try
			{
				if (!Directory.Exists(fileSystemPath) && !File.Exists(fileSystemPath))
					return WorkspaceMutationResult.Failure("not_found", "Path does not exist.");

				var fileName = Path.GetFileName(fileSystemPath);
				long? sizeBytes = null;
				if (File.Exists(fileSystemPath))
				{
					try { sizeBytes = new FileInfo(fileSystemPath).Length; } catch { }
				}

				AddToRecentlyClosed(normalizedPath, fileName, sizeBytes);

				if (Directory.Exists(fileSystemPath))
				{
					foreach (var tab in _state.OpenTabs)
					{
						if (tab.FilePath.StartsWith(normalizedPath, StringComparison.Ordinal))
						{
							RemoveRecoverySnapshot(tab.DocumentId);
							_draftContents.Remove(tab.DocumentId);
						}
					}
					Directory.Delete(fileSystemPath, true);
				}
				else
				{
					RemoveRecoverySnapshot(normalizedPath);
					_draftContents.Remove(normalizedPath);
					File.Delete(fileSystemPath);
				}

				RefreshWorkspaceFromDisk();
				return WorkspaceMutationResult.Success("removed", normalizedPath);
			}
			catch (Exception ex)
			{
				return WorkspaceMutationResult.Failure("io_error", $"Failed to remove node: {ex.Message}");
			}
		}

		public WorkspaceMutationResult MoveNode(string sourcePath, string targetDirectoryPath)
		{
			if (string.IsNullOrWhiteSpace(_state.WorkspaceRoot))
				return WorkspaceMutationResult.Failure("outside_workspace", "No workspace is open.");

			var normalizedSource = NormalizePath(sourcePath);
			var normalizedTargetDir = NormalizePath(targetDirectoryPath);

			if (!IsUnderWorkspaceRoot(normalizedSource, out var srcOutside) || srcOutside)
				return WorkspaceMutationResult.Failure("outside_workspace", "Source path is outside the workspace.");
			if (!IsUnderWorkspaceRoot(normalizedTargetDir, out var tgtOutside) || tgtOutside)
				return WorkspaceMutationResult.Failure("outside_workspace", "Target directory is outside the workspace.");

			if (IsInsideDotMuseFolder(normalizedTargetDir))
				return WorkspaceMutationResult.Failure("forbidden_path", "Cannot move nodes into the .muse workspace folder.");

			var srcFsPath = normalizedSource.Replace('/', Path.DirectorySeparatorChar);
			var tgtDirFsPath = normalizedTargetDir.Replace('/', Path.DirectorySeparatorChar);

			if (normalizedTargetDir.StartsWith(normalizedSource + "/", StringComparison.Ordinal) || normalizedTargetDir == normalizedSource)
				return WorkspaceMutationResult.Failure("path_conflict", "Cannot move a directory into itself.");

			var sourceName = Path.GetFileName(srcFsPath);
			var destPath = Path.Combine(tgtDirFsPath, sourceName);
			var normalizedDest = NormalizePath(destPath);

			if (!IsUnderWorkspaceRoot(normalizedDest, out _))
				return WorkspaceMutationResult.Failure("outside_workspace", "Destination path is outside the workspace.");

			foreach (var tab in _state.OpenTabs)
			{
				if (tab.FilePath.StartsWith(normalizedSource, StringComparison.Ordinal))
				{
					if (tab.IsDirty)
						return WorkspaceMutationResult.Failure("open_tab_unsaved", "The file or directory is open in a tab with unsaved changes.");
					return WorkspaceMutationResult.Failure("open_tab_unsaved", "The file or directory is currently open. Use CloseAndMove to move it first.");
				}
			}

			try
			{
				if (!Directory.Exists(srcFsPath) && !File.Exists(srcFsPath))
					return WorkspaceMutationResult.Failure("not_found", "Source path does not exist.");

				if (Directory.Exists(destPath) || File.Exists(destPath))
					return WorkspaceMutationResult.Failure("path_conflict", "A file or directory already exists at the destination.");

				if (!Directory.Exists(tgtDirFsPath))
					Directory.CreateDirectory(tgtDirFsPath);

				if (Directory.Exists(srcFsPath))
				{
					Directory.Move(srcFsPath, destPath);
				}
				else
				{
					File.Move(srcFsPath, destPath);
				}

				UpdateTabPaths(normalizedSource, normalizedDest);
				UpdateDraftKeys(normalizedSource, normalizedDest);
				UpdateRecoverySnapshots(normalizedSource, normalizedDest);

				RefreshWorkspaceFromDisk();
				return WorkspaceMutationResult.Success("moved", normalizedDest);
			}
			catch (Exception ex)
			{
				return WorkspaceMutationResult.Failure("io_error", $"Failed to move node: {ex.Message}");
			}
		}

		// --- S2-009: Atomic soft-flow operations ---

		public WorkspaceMutationResult CloseAndRemove(string path)
		{
			var normalizedPath = NormalizePath(path);
			var fileSystemPath = normalizedPath.Replace('/', Path.DirectorySeparatorChar);

			try
			{
				var tab = _state.OpenTabs.FirstOrDefault(t => t.FilePath == normalizedPath);
				if (tab is not null)
				{
					if (tab.IsDirty)
					{
						var draftContent = GetDraftContent(tab.DocumentId);
						if (draftContent is not null)
						{
							try { File.WriteAllText(fileSystemPath, draftContent); }
							catch { }
						}
					}
					CloseDocument(tab.DocumentId);
				}

				if (File.Exists(fileSystemPath))
				{
					AddToRecentlyClosed(normalizedPath, Path.GetFileName(fileSystemPath), null);
					File.Delete(fileSystemPath);
				}
				else if (Directory.Exists(fileSystemPath))
				{
					Directory.Delete(fileSystemPath, true);
				}

				RemoveRecoverySnapshot(normalizedPath);
				_draftContents.Remove(normalizedPath);
				RefreshWorkspaceFromDisk();
				return WorkspaceMutationResult.Success("closed_and_removed", normalizedPath);
			}
			catch (Exception ex)
			{
				return WorkspaceMutationResult.Failure("io_error", $"Failed to close and remove: {ex.Message}");
			}
		}

		public WorkspaceMutationResult CloseAndMove(string path, string targetDirectoryPath)
		{
			var normalizedPath = NormalizePath(path);
			var normalizedTargetDir = NormalizePath(targetDirectoryPath);
			var srcFsPath = normalizedPath.Replace('/', Path.DirectorySeparatorChar);
			var tgtDirFsPath = normalizedTargetDir.Replace('/', Path.DirectorySeparatorChar);

			if (!IsUnderWorkspaceRoot(normalizedTargetDir, out _))
				return WorkspaceMutationResult.Failure("outside_workspace", "Target directory is outside the workspace.");

			var sourceName = Path.GetFileName(srcFsPath);
			var destPath = Path.Combine(tgtDirFsPath, sourceName);
			var normalizedDest = NormalizePath(destPath);

			try
			{
				if (Directory.Exists(destPath) || File.Exists(destPath))
					return WorkspaceMutationResult.Failure("path_conflict", "A file or directory already exists at the destination.");

				var tab = _state.OpenTabs.FirstOrDefault(t => t.FilePath == normalizedPath);
				if (tab is not null)
				{
					if (tab.IsDirty)
					{
						var draftContent = GetDraftContent(tab.DocumentId);
						if (draftContent is not null)
						{
							try { File.WriteAllText(srcFsPath, draftContent); }
							catch { }
						}
					}
					CloseDocument(tab.DocumentId);
				}

				if (!Directory.Exists(tgtDirFsPath))
					Directory.CreateDirectory(tgtDirFsPath);

				if (Directory.Exists(srcFsPath))
				{
					Directory.Move(srcFsPath, destPath);
				}
				else if (File.Exists(srcFsPath))
				{
					File.Move(srcFsPath, destPath);
				}

				RemoveRecoverySnapshot(normalizedPath);
				_draftContents.Remove(normalizedPath);
				RefreshWorkspaceFromDisk();
				return WorkspaceMutationResult.Success("closed_and_moved", normalizedDest);
			}
			catch (Exception ex)
			{
				return WorkspaceMutationResult.Failure("io_error", $"Failed to close and move: {ex.Message}");
			}
		}

		// --- S2-009: Session persistence ---

		public WorkspaceSessionState? GetLastSession()
		{
			if (string.IsNullOrWhiteSpace(_state.WorkspaceRoot))
				return null;

			var path = GetSessionPath(_state.WorkspaceRoot);
			try
			{
				if (!File.Exists(path))
					return null;
				var json = File.ReadAllText(path);
				return JsonSerializer.Deserialize<WorkspaceSessionState>(json);
			}
			catch
			{
				return null;
			}
		}

		public void FlushSession()
		{
			if (string.IsNullOrWhiteSpace(_state.WorkspaceRoot))
				return;

			var normalizedRoot = NormalizePath(_state.WorkspaceRoot);
			var ids = _state.OpenTabs.Select(t => t.DocumentId).ToArray();
			var session = new WorkspaceSessionState(normalizedRoot, ids, DateTimeOffset.UtcNow);
			WriteSession(normalizedRoot, session);
		}

		public void InvalidateSession()
		{
			if (string.IsNullOrWhiteSpace(_state.WorkspaceRoot))
				return;

			var path = GetSessionPath(_state.WorkspaceRoot);
			try
			{
				if (File.Exists(path))
					File.Delete(path);
			}
			catch
			{
			}
		}

		// --- S2-009: Recently closed ---

		public IReadOnlyList<RecentlyClosedEntry> GetRecentlyClosed()
		{
			return LoadRecentlyClosedFromDisk();
		}

		public void RemoveFromRecentlyClosed(string path)
		{
			var normalizedPath = NormalizePath(path);
			var entries = LoadRecentlyClosedFromDisk().ToList();
			var removed = entries.RemoveAll(e => e.FilePath == normalizedPath);
			if (removed > 0)
				SaveRecentlyClosedToDisk(entries);
		}

		// --- S2-009: Helper methods ---

		private static WorkspaceMutationResult? ValidateNodeName(string name)
		{
			if (string.IsNullOrWhiteSpace(name))
				return WorkspaceMutationResult.Failure("invalid_name", "Name cannot be empty or whitespace.");

			if (name.StartsWith('.'))
				return WorkspaceMutationResult.Failure("invalid_name", "Name cannot start with a dot.");

			var invalidChars = Path.GetInvalidFileNameChars();
			if (name.Any(c => invalidChars.Contains(c)))
				return WorkspaceMutationResult.Failure("invalid_name", "Name contains invalid characters.");

			return null;
		}

		private bool IsUnderWorkspaceRoot(string normalizedPath, out bool outside)
		{
			outside = false;
			if (string.IsNullOrWhiteSpace(_state.WorkspaceRoot))
				return false;

			var root = NormalizePath(_state.WorkspaceRoot);
			var rootWithSlash = root + "/";
			if (normalizedPath == root || normalizedPath.StartsWith(rootWithSlash, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
			outside = true;
			return false;
		}

		private static bool IsInsideDotMuseFolder(string normalizedPath)
		{
			return normalizedPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
				.Any(segment => string.Equals(segment, InternalWorkspaceFolderName, StringComparison.OrdinalIgnoreCase));
		}

		private void UpdateTabPaths(string oldPath, string newPath)
		{
			var updated = new List<WorkspaceTabState>();
			foreach (var tab in _state.OpenTabs)
			{
				if (tab.FilePath.StartsWith(oldPath, StringComparison.Ordinal))
				{
					var relativePart = tab.FilePath[oldPath.Length..];
					var newFilePath = newPath + relativePart;
					var newDocumentId = tab.DocumentId.StartsWith(oldPath, StringComparison.Ordinal)
						? newPath + tab.DocumentId[oldPath.Length..]
						: tab.DocumentId;
					updated.Add(new WorkspaceTabState(newDocumentId, newFilePath, tab.IsDirty, tab.LastTouchedAt)
					{
						HasExternalConflict = tab.HasExternalConflict,
						ConflictMessage = tab.ConflictMessage,
						HasUnsavedRecovery = tab.HasUnsavedRecovery,
						IsMissingOnDisk = tab.IsMissingOnDisk
					});
				}
				else
				{
					updated.Add(tab);
				}
			}
			_state = _state with { OpenTabs = updated };
		}

		private void UpdateDraftKeys(string oldPath, string newPath)
		{
			var keysToUpdate = _draftContents.Keys
				.Where(k => k.StartsWith(oldPath, StringComparison.Ordinal))
				.ToList();

			foreach (var key in keysToUpdate)
			{
				var relativePart = key[oldPath.Length..];
				var newKey = newPath + relativePart;
				_draftContents[newKey] = _draftContents[key];
				_draftContents.Remove(key);
			}
		}

		private void UpdateRecoverySnapshots(string oldPath, string newPath)
		{
			if (string.IsNullOrWhiteSpace(_state.WorkspaceRoot))
				return;

			var recoveryDir = GetRecoveryDirectory(_state.WorkspaceRoot);
			if (!Directory.Exists(recoveryDir))
				return;

			foreach (var tab in _state.OpenTabs)
			{
				if (tab.FilePath.StartsWith(newPath, StringComparison.Ordinal))
				{
					var oldDocId = oldPath + tab.DocumentId[newPath.Length..];
					RemoveRecoverySnapshot(oldDocId);

					var content = GetDraftContent(tab.DocumentId);
					if (content is not null)
					{
						var snapshot = new WorkspaceRecoverySnapshot(tab.DocumentId, tab.FilePath, content, DateTimeOffset.UtcNow);
						var snapshotPath = GetRecoverySnapshotPath(recoveryDir, tab.DocumentId);
						Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
						File.WriteAllText(snapshotPath, JsonSerializer.Serialize(snapshot));
					}
				}
			}
		}

		private void AddToRecentlyClosed(string normalizedPath, string fileName, long? sizeBytes)
		{
			var entries = LoadRecentlyClosedFromDisk().ToList();
			var entry = new RecentlyClosedEntry(normalizedPath, fileName, DateTimeOffset.UtcNow, sizeBytes);
			entries.Insert(0, entry);

			if (entries.Count > 20)
				entries = entries.Take(20).ToList();

			SaveRecentlyClosedToDisk(entries);
		}

		private void WriteSession(string normalizedRoot, WorkspaceSessionState session)
		{
			var settingsDir = GetSettingsDirectory(normalizedRoot);
			Directory.CreateDirectory(settingsDir);
			var path = GetSessionPath(normalizedRoot);
			var tmpPath = path + ".tmp";
			File.WriteAllText(tmpPath, JsonSerializer.Serialize(session));
			File.Move(tmpPath, path, overwrite: true);
		}

		private static string GetSessionPath(string normalizedRoot)
		{
			return Path.Combine(GetSettingsDirectory(normalizedRoot), "session.json");
		}

		private static WorkspaceSessionState? LoadSession(string normalizedRoot)
		{
			try
			{
				var path = GetSessionPath(normalizedRoot);
				if (!File.Exists(path)) return null;
				var json = File.ReadAllText(path);
				return JsonSerializer.Deserialize<WorkspaceSessionState>(json);
			}
			catch
			{
				return null;
			}
		}

		private static string GetRecentlyClosedPath(string normalizedRoot)
		{
			return Path.Combine(GetSettingsDirectory(normalizedRoot), "recently-closed.json");
		}

		private IReadOnlyList<RecentlyClosedEntry> LoadRecentlyClosedFromDisk()
		{
			if (string.IsNullOrWhiteSpace(_state.WorkspaceRoot))
				return Array.Empty<RecentlyClosedEntry>();

			var path = GetRecentlyClosedPath(_state.WorkspaceRoot);
			try
			{
				if (!File.Exists(path))
					return Array.Empty<RecentlyClosedEntry>();
				var json = File.ReadAllText(path);
				return JsonSerializer.Deserialize<List<RecentlyClosedEntry>>(json) ?? new List<RecentlyClosedEntry>();
			}
			catch
			{
				return Array.Empty<RecentlyClosedEntry>();
			}
		}

		private void SaveRecentlyClosedToDisk(List<RecentlyClosedEntry> entries)
		{
			if (string.IsNullOrWhiteSpace(_state.WorkspaceRoot))
				return;

			var path = GetRecentlyClosedPath(_state.WorkspaceRoot);
			var settingsDir = Path.GetDirectoryName(path)!;
			Directory.CreateDirectory(settingsDir);
			File.WriteAllText(path, JsonSerializer.Serialize(entries));
		}

	private void AppendConflictEvent(string documentId, string action, string message)
	{
		_conflictEvents.Add(new ConflictEvent(documentId, action, message, DateTimeOffset.UtcNow));
		if (_conflictEvents.Count > 100)
		{
			_conflictEvents.RemoveRange(0, _conflictEvents.Count - 100);
		}
	}

	private static readonly string[] _wellKnownTextExtensions = new[] { ".md", ".markdown", ".txt", ".json", ".csv", ".yml", ".yaml" };

	private static bool IsFileSupported(string path, out string? reason)
	{
		reason = null;
		if (string.IsNullOrWhiteSpace(path))
		{
			reason = "无效的文件路径。";
			return false;
		}

		var ext = Path.GetExtension(path)?.ToLowerInvariant();
		if (!string.IsNullOrWhiteSpace(ext) && _wellKnownTextExtensions.Contains(ext))
		{
			return true;
		}

		try
		{
			using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
			var buffer = new byte[4096];
			var read = fs.Read(buffer, 0, buffer.Length);
			for (int i = 0; i < read; i++)
			{
				if (buffer[i] == 0)
				{
					reason = "不支持的二进制文件格式。";
					return false;
				}
			}
			return true;
		}
		catch
		{
			reason = "无法读取文件以确定格式。";
			return false;
		}
	}

}
