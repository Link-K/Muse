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

		public WorkspaceState OpenWorkspace(string rootPath)
		{
			if (_openWorkspaceStates.Count > 0)
			{
				_state = _openWorkspaceStates.Dequeue();
			}

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
					return tabs[i];
				}
			}

			return null;
		}

		public WorkspaceState GetState()
		{
			return _state;
		}
	}
}
