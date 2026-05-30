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
	private bool _isHydratingDraft;

	public MainViewModel()
		: this(new MarkdownPreviewService(), new InMemoryWorkspaceService(enableBackgroundAutoSave: true))
	{
	}

	internal MainViewModel(IMarkdownPreviewService previewService, IWorkspaceService workspaceService)
	{
		_previewService = previewService;
		_workspaceService = workspaceService;
		_workspaceService.WorkspaceChanged += HandleWorkspaceChanged;
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

	[ObservableProperty]
	private string? _saveFeedbackMessage;

	[ObservableProperty]
	private bool _saveFeedbackIsError;

	[ObservableProperty]
	private DateTimeOffset? _lastSavedAt;

	[ObservableProperty]
	private string _lastSaveStatus = "未保存";

	[ObservableProperty]
	private bool _activeDocumentHasExternalConflict;

	[ObservableProperty]
	private string? _activeDocumentConflictMessage;

	[ObservableProperty]
	private string? _latestConflictEventMessage;

	[ObservableProperty]
	private string[] _recentConflictEventMessages = [];

	[ObservableProperty]
	private bool _isConflictLogExpanded;

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

	public string ActiveDocumentConflictText => ActiveDocumentHasExternalConflict ? (ActiveDocumentConflictMessage ?? "检测到外部文件变更。") : "";

	public bool HasActiveDocumentConflict => ActiveDocumentHasExternalConflict;

	public bool HasLatestConflictEvent => !string.IsNullOrWhiteSpace(LatestConflictEventMessage);

	public bool HasRecentConflictEvents => RecentConflictEventMessages.Length > 0;

	public string ConflictLogToggleText => IsConflictLogExpanded
		? "收起最近冲突日志"
		: $"展开最近冲突日志（{RecentConflictEventMessages.Length}）";

	public bool ShowExpandedConflictLogPanel => IsConflictLogExpanded && HasRecentConflictEvents;

	public bool CanSaveActiveDocument => ActiveDocumentIsDirty && !string.IsNullOrWhiteSpace(_workspaceService.GetState().ActiveDocumentId);

	public bool CanResolveActiveConflict => HasActiveDocumentConflict && !string.IsNullOrWhiteSpace(_workspaceService.GetState().ActiveDocumentId);

	public bool HasSaveFeedback => !string.IsNullOrWhiteSpace(SaveFeedbackMessage);

	public string SaveFeedbackForeground => SaveFeedbackIsError ? "#D13438" : "#107C10";

	public string LastSavedAtDisplay => LastSavedAt.HasValue
		? LastSavedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
		: "从未保存";

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
			SaveFeedbackIsError = true;
			SaveFeedbackMessage = "保存失败：当前没有活动文档。";
			LastSaveStatus = "保存失败";
			return;
		}

		var result = _workspaceService.SaveDocument(activeDocumentId, MarkdownDraft);
		if (!result.Succeeded)
		{
			SaveFeedbackIsError = true;
			SaveFeedbackMessage = $"保存失败：{result.Message}";
			LastSaveStatus = $"保存失败（{result.Code}）";
			return;
		}

		SyncWorkspaceState();
		LastSavedAt = result.Tab?.LastTouchedAt ?? DateTimeOffset.UtcNow;
		LastSaveStatus = "保存成功";
		SaveFeedbackIsError = false;
		SaveFeedbackMessage = "保存成功。";
	}

	[RelayCommand]
	private void ResolveConflictBySavingLocal()
	{
		var activeDocumentId = _workspaceService.GetState().ActiveDocumentId;
		if (string.IsNullOrWhiteSpace(activeDocumentId))
		{
			return;
		}

		var result = _workspaceService.ResolveConflictBySavingLocal(activeDocumentId, MarkdownDraft);
		if (!result.Succeeded)
		{
			SaveFeedbackIsError = true;
			SaveFeedbackMessage = $"冲突处置失败：{result.Message}";
			return;
		}

		SyncWorkspaceState();
		LastSavedAt = result.Tab?.LastTouchedAt ?? DateTimeOffset.UtcNow;
		LastSaveStatus = "已保留本地并覆盖保存";
		SaveFeedbackIsError = false;
		SaveFeedbackMessage = "已使用本地内容覆盖外部文件。";
	}

	[RelayCommand]
	private void ResolveConflictByReloadingExternal()
	{
		var activeDocumentId = _workspaceService.GetState().ActiveDocumentId;
		if (string.IsNullOrWhiteSpace(activeDocumentId))
		{
			return;
		}

		var result = _workspaceService.ResolveConflictByReloadingFromDisk(activeDocumentId);
		if (!result.Succeeded)
		{
			SaveFeedbackIsError = true;
			SaveFeedbackMessage = $"冲突处置失败：{result.Message}";
			return;
		}

		var draftContent = _workspaceService.GetDraftContent(activeDocumentId);
		if (draftContent is not null)
		{
			try
			{
				_isHydratingDraft = true;
				MarkdownDraft = draftContent;
			}
			finally
			{
				_isHydratingDraft = false;
			}
		}

		SyncWorkspaceState();
		LastSavedAt = result.Tab?.LastTouchedAt ?? DateTimeOffset.UtcNow;
		LastSaveStatus = "已丢弃本地并重载外部";
		SaveFeedbackIsError = false;
		SaveFeedbackMessage = "已重载外部文件内容。";
	}

	[RelayCommand]
	private void ToggleConflictLogExpanded()
	{
		IsConflictLogExpanded = !IsConflictLogExpanded;
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
		if (_isHydratingDraft)
		{
			return;
		}

		UpdateActiveDocumentDraft(value);
		LastSaveStatus = "有未保存更改";
		SaveFeedbackMessage = null;
	}

	partial void OnLastSavedAtChanged(DateTimeOffset? value)
	{
		OnPropertyChanged(nameof(LastSavedAtDisplay));
	}

	partial void OnSaveFeedbackMessageChanged(string? value)
	{
		OnPropertyChanged(nameof(HasSaveFeedback));
	}

	partial void OnLatestConflictEventMessageChanged(string? value)
	{
		OnPropertyChanged(nameof(HasLatestConflictEvent));
	}

	partial void OnRecentConflictEventMessagesChanged(string[] value)
	{
		OnPropertyChanged(nameof(HasRecentConflictEvents));
		OnPropertyChanged(nameof(ConflictLogToggleText));
		OnPropertyChanged(nameof(ShowExpandedConflictLogPanel));
	}

	partial void OnIsConflictLogExpandedChanged(bool value)
	{
		OnPropertyChanged(nameof(ConflictLogToggleText));
		OnPropertyChanged(nameof(ShowExpandedConflictLogPanel));
	}

	partial void OnSaveFeedbackIsErrorChanged(bool value)
	{
		OnPropertyChanged(nameof(SaveFeedbackForeground));
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
		OnPropertyChanged(nameof(CanResolveActiveConflict));
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
		LoadActiveDocumentContent();
	}

	private void LoadActiveDocumentContent()
	{
		var state = _workspaceService.GetState();
		var activeTab = state.OpenTabs.FirstOrDefault(tab => tab.DocumentId == state.ActiveDocumentId);
		if (activeTab is null)
		{
			return;
		}

		var draftContent = _workspaceService.GetDraftContent(activeTab.DocumentId);
		if (!string.IsNullOrWhiteSpace(draftContent))
		{
			try
			{
				_isHydratingDraft = true;
				MarkdownDraft = draftContent;
			}
			finally
			{
				_isHydratingDraft = false;
			}
			return;
		}

		var filePath = activeTab.FilePath.Replace('/', Path.DirectorySeparatorChar);
		if (!File.Exists(filePath))
		{
			return;
		}

		try
		{
			_isHydratingDraft = true;
			MarkdownDraft = File.ReadAllText(filePath);
		}
		catch (Exception ex)
		{
			SaveFeedbackIsError = true;
			SaveFeedbackMessage = $"读取文档失败：{ex.Message}";
		}
		finally
		{
			_isHydratingDraft = false;
		}
	}

	private void HandleWorkspaceChanged(object? sender, EventArgs e)
	{
		SyncWorkspaceState();
		RefreshConflictEventPresentation();

		var state = _workspaceService.GetState();
		var activeTab = state.OpenTabs.FirstOrDefault(tab => tab.DocumentId == state.ActiveDocumentId);
		if (activeTab is null)
		{
			return;
		}

		ActiveDocumentHasExternalConflict = activeTab.HasExternalConflict;
		ActiveDocumentConflictMessage = activeTab.ConflictMessage;
		OnPropertyChanged(nameof(ActiveDocumentConflictText));
		OnPropertyChanged(nameof(HasActiveDocumentConflict));

		if (activeTab.IsDirty)
		{
			if (activeTab.HasExternalConflict)
			{
				LastSaveStatus = "检测到外部文件变更";
				SaveFeedbackIsError = true;
				SaveFeedbackMessage = activeTab.ConflictMessage ?? "检测到外部文件变更，当前草稿未被回填。";
			}
			return;
		}

		var draftContent = _workspaceService.GetDraftContent(activeTab.DocumentId);
		if (draftContent is null || draftContent == MarkdownDraft)
		{
			return;
		}

		try
		{
			_isHydratingDraft = true;
			MarkdownDraft = draftContent;
		}
		finally
		{
			_isHydratingDraft = false;
		}
	}

	private void RefreshConflictEventPresentation()
	{
		var recentEvents = _workspaceService
			.GetConflictEvents()
			.OrderByDescending(item => item.OccurredAt)
			.Take(5)
			.ToArray();

		LatestConflictEventMessage = recentEvents.Length == 0
			? null
			: $"冲突日志：{recentEvents[0].Message}（{recentEvents[0].OccurredAt.ToLocalTime():HH:mm:ss}）";

		RecentConflictEventMessages = recentEvents
			.Select(item => $"[{item.OccurredAt.ToLocalTime():HH:mm:ss}] {item.Message}")
			.ToArray();

		if (recentEvents.Length == 0)
		{
			IsConflictLogExpanded = false;
		}
	}

	private void UpdateActiveDocumentDraft(string content)
	{
		var state = _workspaceService.GetState();
		if (string.IsNullOrWhiteSpace(state.ActiveDocumentId))
		{
			return;
		}

		_workspaceService.UpdateDocumentDraft(state.ActiveDocumentId, content);
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
		ActiveDocumentHasExternalConflict = activeTab?.HasExternalConflict ?? false;
		ActiveDocumentConflictMessage = activeTab?.ConflictMessage;
		OnPropertyChanged(nameof(CanSaveActiveDocument));
		OnPropertyChanged(nameof(ActiveDocumentConflictText));
		OnPropertyChanged(nameof(HasActiveDocumentConflict));
		OnPropertyChanged(nameof(WorkspaceSummary));
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
