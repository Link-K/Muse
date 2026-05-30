using System;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Muse.Rendering;
using Muse.Workspace;

namespace Muse.ViewModels;

public partial class MainViewModel : ViewModelBase
{
	private readonly IMarkdownPreviewService _previewService;
	private readonly IWorkspaceService _workspaceService;

	public MainViewModel()
		: this(new MarkdownPreviewService(), new InMemoryWorkspaceService())
	{
	}

	internal MainViewModel(IMarkdownPreviewService previewService, IWorkspaceService workspaceService)
	{
		_previewService = previewService;
		_workspaceService = workspaceService;
		LoadWorkspace(Environment.CurrentDirectory);
		TryOpenDefaultTaskDocument();
		RefreshPreview();
	}

	[ObservableProperty]
	private EditorMode _currentMode = EditorMode.Edit;

	[ObservableProperty]
	private SplitOrientation _currentSplitOrientation = SplitOrientation.Horizontal;

	[ObservableProperty]
	private string _markdownDraft = "# Muse\n\n在这里开始写 Markdown。";

	[ObservableProperty]
	private int _splitSourceCaretIndex;

	[ObservableProperty]
	private string _previewText = "预览区（占位）：当前无内容";

	[ObservableProperty]
	private string? _previewDiagnostic;

	[ObservableProperty]
	private string _workspaceRootDisplay = "未加载";

	[ObservableProperty]
	private int _openTabsCount;

	[ObservableProperty]
	private string _activeDocumentDisplay = "无";

	[ObservableProperty]
	private bool _activeDocumentIsDirty;

	public string HeaderText => CurrentMode switch
	{
		EditorMode.Edit => "编辑模式（默认）",
		EditorMode.Split => "分屏模式",
		_ => "阅读模式"
	};

	public bool IsEditMode => CurrentMode == EditorMode.Edit;

	public bool IsSplitMode => CurrentMode == EditorMode.Split;

	public bool IsReadMode => CurrentMode == EditorMode.Read;

	public bool IsSplitHorizontal => CurrentSplitOrientation == SplitOrientation.Horizontal;

	public bool IsSplitVertical => CurrentSplitOrientation == SplitOrientation.Vertical;

	public string SplitOrientationText => CurrentSplitOrientation == SplitOrientation.Horizontal
		? "当前分屏：左右"
		: "当前分屏：上下";

	public string PreviewPlaceholder => PreviewText;

	public bool HasPreviewDiagnostic => !string.IsNullOrWhiteSpace(PreviewDiagnostic);

	public string ActiveDocumentDirtyText => ActiveDocumentIsDirty ? "脏状态：已修改" : "脏状态：已保存";

	public bool CanSaveActiveDocument => ActiveDocumentIsDirty && !string.IsNullOrWhiteSpace(_workspaceService.GetState().ActiveDocumentId);

	public string WorkspaceSummary => $"工作区：{WorkspaceRootDisplay} | 标签：{OpenTabsCount} | 当前：{ActiveDocumentDisplay} | {ActiveDocumentDirtyText}";

	[RelayCommand]
	private void SwitchToEditMode()
	{
		CurrentMode = EditorMode.Edit;
	}

	[RelayCommand]
	private void SwitchToSplitMode()
	{
		CurrentMode = EditorMode.Split;
	}

	[RelayCommand]
	private void SwitchToReadMode()
	{
		CurrentMode = EditorMode.Read;
	}

	[RelayCommand]
	private void ToggleSplitOrientation()
	{
		CurrentSplitOrientation = CurrentSplitOrientation == SplitOrientation.Horizontal
			? SplitOrientation.Vertical
			: SplitOrientation.Horizontal;
	}

	[RelayCommand]
	private void OpenCurrentWorkspace()
	{
		LoadWorkspace(Environment.CurrentDirectory);
	}

	[RelayCommand]
	private void OpenSprintTaskDocument()
	{
		TryOpenDefaultTaskDocument();
	}

	[RelayCommand]
	private void SaveActiveDocument()
	{
		var activeDocumentId = _workspaceService.GetState().ActiveDocumentId;
		if (string.IsNullOrWhiteSpace(activeDocumentId))
		{
			return;
		}

		_workspaceService.SaveDocument(activeDocumentId);
		SyncWorkspaceState();
	}

	partial void OnCurrentModeChanged(EditorMode value)
	{
		OnPropertyChanged(nameof(HeaderText));
		OnPropertyChanged(nameof(IsEditMode));
		OnPropertyChanged(nameof(IsSplitMode));
		OnPropertyChanged(nameof(IsReadMode));
		RefreshPreview();
	}

	partial void OnCurrentSplitOrientationChanged(SplitOrientation value)
	{
		OnPropertyChanged(nameof(IsSplitHorizontal));
		OnPropertyChanged(nameof(IsSplitVertical));
		OnPropertyChanged(nameof(SplitOrientationText));
	}

	partial void OnMarkdownDraftChanged(string value)
	{
		RefreshPreview();
		MarkActiveDocumentDirty();
	}

	partial void OnPreviewTextChanged(string value)
	{
		OnPropertyChanged(nameof(PreviewPlaceholder));
	}

	partial void OnPreviewDiagnosticChanged(string? value)
	{
		OnPropertyChanged(nameof(HasPreviewDiagnostic));
	}

	partial void OnWorkspaceRootDisplayChanged(string value)
	{
		OnPropertyChanged(nameof(WorkspaceSummary));
	}

	partial void OnOpenTabsCountChanged(int value)
	{
		OnPropertyChanged(nameof(WorkspaceSummary));
	}

	partial void OnActiveDocumentDisplayChanged(string value)
	{
		OnPropertyChanged(nameof(WorkspaceSummary));
	}

	partial void OnActiveDocumentIsDirtyChanged(bool value)
	{
		OnPropertyChanged(nameof(ActiveDocumentDirtyText));
		OnPropertyChanged(nameof(CanSaveActiveDocument));
		OnPropertyChanged(nameof(WorkspaceSummary));
	}

	private void RefreshPreview()
	{
		var viewState = _previewService.Build(MarkdownDraft, CurrentMode, "light");
		PreviewText = viewState.PreviewText;
		PreviewDiagnostic = viewState.DiagnosticMessage;
	}

	private void LoadWorkspace(string rootPath)
	{
		_workspaceService.OpenWorkspace(rootPath);
		SyncWorkspaceState();
	}

	private void TryOpenDefaultTaskDocument()
	{
		if (string.IsNullOrWhiteSpace(WorkspaceRootDisplay) || WorkspaceRootDisplay == "未加载")
		{
			return;
		}

		var normalizedRoot = WorkspaceRootDisplay.Replace('/', Path.DirectorySeparatorChar);
		var sprintTaskPath = Path.Combine(normalizedRoot, "files", "Sprint2-任务卡.md");
		if (!File.Exists(sprintTaskPath))
		{
			return;
		}

		_workspaceService.OpenDocument(sprintTaskPath);
		SyncWorkspaceState();
	}

	private void MarkActiveDocumentDirty()
	{
		var state = _workspaceService.GetState();
		if (string.IsNullOrWhiteSpace(state.ActiveDocumentId))
		{
			return;
		}

		_workspaceService.MarkDirty(state.ActiveDocumentId, true);
		SyncWorkspaceState();
	}

	private void SyncWorkspaceState()
	{
		var state = _workspaceService.GetState();
		var activeTab = state.OpenTabs.FirstOrDefault(tab => tab.DocumentId == state.ActiveDocumentId);
		WorkspaceRootDisplay = state.WorkspaceRoot ?? "未加载";
		OpenTabsCount = state.OpenTabs.Count;
		ActiveDocumentDisplay = state.ActiveDocumentId ?? "无";
		ActiveDocumentIsDirty = activeTab?.IsDirty ?? false;
		OnPropertyChanged(nameof(CanSaveActiveDocument));
	}
}

public enum EditorMode
{
	Edit,
	Split,
	Read
}

public enum SplitOrientation
{
	Horizontal,
	Vertical
}
