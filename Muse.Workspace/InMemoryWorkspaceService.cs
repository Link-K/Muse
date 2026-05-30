namespace Muse.Workspace;

public sealed class InMemoryWorkspaceService : IWorkspaceService
{
	private readonly IAutoSaveScheduler _autoSaveScheduler;
	private WorkspaceState _state = new(null, [], [], null);

	public InMemoryWorkspaceService(IAutoSaveScheduler? autoSaveScheduler = null)
	{
		_autoSaveScheduler = autoSaveScheduler ?? new MemoryAutoSaveScheduler();
	}

	public WorkspaceState OpenWorkspace(string rootPath)
	{
		var normalizedRoot = NormalizePath(rootPath);
		IReadOnlyList<FileTreeNode> fileTree = Directory.Exists(normalizedRoot)
			? [BuildTree(normalizedRoot)]
			: Array.Empty<FileTreeNode>();

		_state = new WorkspaceState(normalizedRoot, fileTree, [], null);
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
}
