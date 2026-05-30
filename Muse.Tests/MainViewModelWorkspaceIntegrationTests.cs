using Muse.Rendering;
using Muse.ViewModels;
using Muse.Workspace;
using Xunit;

namespace Muse.Tests;

public sealed class MainViewModelWorkspaceIntegrationTests
{
	[Fact]
	public void OpenCurrentWorkspaceCommand_ShouldRefreshWorkspaceSummary()
	{
		var preview = new FakePreviewService();
		var workspace = new FakeWorkspaceService(
			new WorkspaceState("D:/repo", [], [], null),
			new WorkspaceState("D:/repo/next", [], [], null));
		var viewModel = new MainViewModel(preview, workspace);

		viewModel.OpenCurrentWorkspaceCommand.Execute(null);

		Assert.Equal("D:/repo/next", viewModel.WorkspaceRootDisplay);
		Assert.Contains("D:/repo/next", viewModel.WorkspaceSummary);
	}

	[Fact]
	public void MarkdownDraftChange_ShouldMarkActiveDocumentDirty()
	{
		var preview = new FakePreviewService();
		var state = new WorkspaceState(
			"D:/repo",
			[],
			[new WorkspaceTabState("doc-1", "D:/repo/files/a.md", false, DateTimeOffset.UtcNow)],
			"doc-1");
		var workspace = new FakeWorkspaceService(state);
		var viewModel = new MainViewModel(preview, workspace);

		viewModel.MarkdownDraft = "changed";

		Assert.Equal("doc-1", workspace.LastMarkedDocumentId);
		Assert.True(viewModel.ActiveDocumentIsDirty);
		Assert.Equal("脏状态：已修改", viewModel.ActiveDocumentDirtyText);

		viewModel.SaveActiveDocumentCommand.Execute(null);

		Assert.Equal("doc-1", workspace.LastSavedDocumentId);
		Assert.False(viewModel.ActiveDocumentIsDirty);
		Assert.Equal("脏状态：已保存", viewModel.ActiveDocumentDirtyText);
		Assert.True(viewModel.HasSaveFeedback);
		Assert.Equal("保存成功。", viewModel.SaveFeedbackMessage);
		Assert.False(viewModel.SaveFeedbackIsError);
		Assert.Equal("保存成功", viewModel.LastSaveStatus);
		Assert.NotEqual("从未保存", viewModel.LastSavedAtDisplay);
	}

	[Fact]
	public void SaveActiveDocument_WhenNoActiveDocument_ShouldShowFailureStatus()
	{
		var preview = new FakePreviewService();
		var workspace = new FakeWorkspaceService(new WorkspaceState("D:/repo", [], [], null));
		var viewModel = new MainViewModel(preview, workspace);

		viewModel.SaveActiveDocumentCommand.Execute(null);

		Assert.Equal("保存失败", viewModel.LastSaveStatus);
		Assert.True(viewModel.SaveFeedbackIsError);
		Assert.Equal("保存失败：当前没有活动文档。", viewModel.SaveFeedbackMessage);
	}

	[Fact]
	public void WorkspaceChangedEvent_ShouldRefreshDraftFromWorkspace()
	{
		var preview = new FakePreviewService();
		var state = new WorkspaceState(
			"D:/repo",
			[],
			[new WorkspaceTabState("doc-1", "D:/repo/files/a.md", false, DateTimeOffset.UtcNow)],
			"doc-1");
		var workspace = new FakeWorkspaceService(state);
		workspace.Drafts["doc-1"] = "External refresh content";
		var viewModel = new MainViewModel(preview, workspace);

		workspace.RaiseWorkspaceChanged();

		Assert.Equal("External refresh content", viewModel.MarkdownDraft);
	}

	[Fact]
	public void WorkspaceChangedEvent_WhenConflictShouldShowWarningAndKeepDraft()
	{
		var preview = new FakePreviewService();
		var state = new WorkspaceState(
			"D:/repo",
			[],
			[new WorkspaceTabState("doc-1", "D:/repo/files/a.md", true, DateTimeOffset.UtcNow)],
			"doc-1");
		var workspace = new FakeWorkspaceService(state);
		var viewModel = new MainViewModel(preview, workspace);
		viewModel.MarkdownDraft = "Local draft";
		workspace.ExternalFileContents["doc-1"] = "External change";

		workspace.RefreshWorkspaceFromDisk();

		Assert.Equal("Local draft", viewModel.MarkdownDraft);
		Assert.True(viewModel.HasActiveDocumentConflict);
		Assert.Contains("外部文件变更", viewModel.ActiveDocumentConflictText);
	}

	private sealed class FakePreviewService : IMarkdownPreviewService
	{
		public PreviewViewState Build(string markdown, EditorMode mode, string? theme = null)
		{
			return new PreviewViewState(markdown, false, null);
		}
	}

	private sealed class FakeWorkspaceService : IWorkspaceService
	{
		private readonly Queue<WorkspaceState> _openWorkspaceStates;
		private WorkspaceState _state;

		public event EventHandler? WorkspaceChanged;

		public FakeWorkspaceService(params WorkspaceState[] openWorkspaceStates)
		{
			if (openWorkspaceStates.Length == 0)
			{
				throw new ArgumentException("At least one state is required.", nameof(openWorkspaceStates));
			}

			_openWorkspaceStates = new Queue<WorkspaceState>(openWorkspaceStates);
			_state = _openWorkspaceStates.Peek();
		}

		public string? LastMarkedDocumentId { get; private set; }

		public string? LastSavedDocumentId { get; private set; }

		public Dictionary<string, string> Drafts { get; } = new(StringComparer.Ordinal);

		public Dictionary<string, string> ExternalFileContents { get; } = new(StringComparer.Ordinal);

		public WorkspaceState OpenWorkspace(string rootPath)
		{
			if (_openWorkspaceStates.Count > 0)
			{
				_state = _openWorkspaceStates.Dequeue();
			}

			RaiseWorkspaceChanged();

			return _state;
		}

		public WorkspaceTabState OpenDocument(string filePath)
		{
			var tab = new WorkspaceTabState(filePath, filePath, false, DateTimeOffset.UtcNow);
			_state = _state with
			{
				OpenTabs = _state.OpenTabs.Concat([tab]).ToArray(),
				ActiveDocumentId = tab.DocumentId
			};
			RaiseWorkspaceChanged();
			return tab;
		}

		public WorkspaceTabState? ActivateDocument(string documentId)
		{
			var tab = _state.OpenTabs.FirstOrDefault(item => item.DocumentId == documentId);
			if (tab is null)
			{
				return null;
			}

			_state = _state with { ActiveDocumentId = documentId };
			RaiseWorkspaceChanged();
			return tab;
		}

		public WorkspaceTabState? MarkDirty(string documentId, bool isDirty = true)
		{
			LastMarkedDocumentId = documentId;
			var tabs = _state.OpenTabs.ToArray();
			for (var i = 0; i < tabs.Length; i++)
			{
				if (tabs[i].DocumentId == documentId)
				{
					tabs[i] = tabs[i] with { IsDirty = isDirty };
					_state = _state with { OpenTabs = tabs };
					RaiseWorkspaceChanged();
					return tabs[i];
				}
			}

			return null;
		}

		public WorkspaceTabState? UpdateDocumentDraft(string documentId, string content)
		{
			Drafts[documentId] = content;
			return MarkDirty(documentId, true);
		}

		public string? GetDraftContent(string documentId)
		{
			return Drafts.TryGetValue(documentId, out var content) ? content : null;
		}

		public void FlushPendingAutoSaves()
		{
			RaiseWorkspaceChanged();
		}

		public WorkspaceState RefreshWorkspaceFromDisk()
		{
			var tabs = _state.OpenTabs.ToArray();
			for (var i = 0; i < tabs.Length; i++)
			{
				var tab = tabs[i];
				if (!ExternalFileContents.TryGetValue(tab.DocumentId, out var content))
				{
					continue;
				}

				var draft = Drafts.TryGetValue(tab.DocumentId, out var draftContent) ? draftContent : null;
				tabs[i] = tab with
				{
					HasExternalConflict = tab.IsDirty && draft is not null && draftContent != content,
					ConflictMessage = tab.IsDirty && draft is not null && draftContent != content ? "检测到外部文件变更，当前草稿尚未同步。" : null
				};
			}

			_state = _state with { OpenTabs = tabs };
			RaiseWorkspaceChanged();
			return _state;
		}

		public SaveDocumentResult SaveDocument(string documentId, string content)
		{
			LastSavedDocumentId = documentId;
			var tabs = _state.OpenTabs.ToArray();
			for (var i = 0; i < tabs.Length; i++)
			{
				if (tabs[i].DocumentId == documentId)
				{
					tabs[i] = tabs[i] with { IsDirty = false };
					_state = _state with { OpenTabs = tabs };
					Drafts[documentId] = content;
					RaiseWorkspaceChanged();
					return SaveDocumentResult.Success(tabs[i]);
				}
			}

			return SaveDocumentResult.Failure("document_not_found", "Document was not found in current workspace tabs.");
		}

		public WorkspaceState GetState()
		{
			return _state;
		}

		public void RaiseWorkspaceChanged()
		{
			WorkspaceChanged?.Invoke(this, EventArgs.Empty);
		}
	}
}
