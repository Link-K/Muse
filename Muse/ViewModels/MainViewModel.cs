using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Muse.Rendering;
using Muse.Workspace;

namespace Muse.ViewModels;

public partial class MainViewModel : ViewModelBase
{
	private readonly IMarkdownPreviewService _previewService;
	private readonly IWorkspaceService _workspaceService;
	private readonly bool _enableConflictLogPreferencePersistence;
	private readonly Timer _conflictLogPreferenceSaveTimer;
	private readonly object _conflictLogPreferenceSaveLock = new();
	private bool _isHydratingDraft;
	private bool _isLoadingConflictLogPreferences;
	private bool _hasPendingConflictLogPreferenceSave;
	private DateTimeOffset _lastConflictLogPreferenceWriteAt = DateTimeOffset.MinValue;
	private const string MuseSettingsDirectoryName = ".muse";
	private const string SettingsDirectoryName = "settings";
	private const string ConflictLogPreferencesFileName = "conflict-log.json";
	private const int ConflictLogPreferenceSaveDebounceMs = 400;
	private const int ConflictLogPreferenceSaveMinIntervalMs = 1500;

	public MainViewModel()
		: this(new MarkdownPreviewService(), new InMemoryWorkspaceService(enableBackgroundAutoSave: true), true)
	{
	}

	internal MainViewModel(IMarkdownPreviewService previewService, IWorkspaceService workspaceService)
		: this(previewService, workspaceService, false)
	{
	}

	internal MainViewModel(IMarkdownPreviewService previewService, IWorkspaceService workspaceService, bool enableConflictLogPreferencePersistence)
	{
		_previewService = previewService;
		_workspaceService = workspaceService;
		_enableConflictLogPreferencePersistence = enableConflictLogPreferencePersistence;
		_conflictLogPreferenceSaveTimer = new Timer(_ => FlushConflictLogPreferencesSave(), null, Timeout.Infinite, Timeout.Infinite);
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
	private ConflictEventListItem[] _recentConflictEvents = [];

	[ObservableProperty]
	private bool _isConflictLogExpanded;

	[ObservableProperty]
	private bool _isConflictLogFilteredToActiveDocument = true;

	[ObservableProperty]
	private int _availableConflictEventCount;

	[ObservableProperty]
	private ConflictEventFilter _selectedConflictEventFilter = ConflictEventFilter.All;

	[ObservableProperty]
	private string _latestConflictEventForeground = "#605E5C";

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

	public bool HasRecentConflictEvents => RecentConflictEvents.Length > 0;

	public bool HasAnyConflictEvents => AvailableConflictEventCount > 0;

	public string ConflictLogToggleText => IsConflictLogExpanded
		? "收起最近冲突日志"
		: $"展开最近冲突日志（{RecentConflictEventMessages.Length}）";

	public string ConflictLogScopeToggleText => IsConflictLogFilteredToActiveDocument
		? "切换为全部文档日志"
		: "切换为当前文档日志";

	public string ConflictLogScopeText => IsConflictLogFilteredToActiveDocument ? "日志范围：当前文档" : "日志范围：全部文档";

	public string ConflictEventFilterToggleText => SelectedConflictEventFilter switch
	{
		ConflictEventFilter.All => "筛选：全部（点此切到检测）",
		ConflictEventFilter.Detected => "筛选：仅检测（点此切到处置）",
		ConflictEventFilter.Resolved => "筛选：仅处置（点此切到失败）",
		ConflictEventFilter.Failed => "筛选：仅失败（点此切到全部）",
		_ => "筛选：全部"
	};

	public string ConflictEventFilterText => SelectedConflictEventFilter switch
	{
		ConflictEventFilter.All => "事件类型：全部",
		ConflictEventFilter.Detected => "事件类型：检测",
		ConflictEventFilter.Resolved => "事件类型：处置",
		ConflictEventFilter.Failed => "事件类型：失败",
		_ => "事件类型：全部"
	};

	public bool CanResetConflictLogFilters => !IsConflictLogFilteredToActiveDocument || SelectedConflictEventFilter != ConflictEventFilter.All;

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

	[RelayCommand]
	private void ToggleConflictLogScope()
	{
		IsConflictLogFilteredToActiveDocument = !IsConflictLogFilteredToActiveDocument;
		RefreshConflictEventPresentation();
	}

	[RelayCommand]
	private void CycleConflictEventFilter()
	{
		SelectedConflictEventFilter = SelectedConflictEventFilter switch
		{
			ConflictEventFilter.All => ConflictEventFilter.Detected,
			ConflictEventFilter.Detected => ConflictEventFilter.Resolved,
			ConflictEventFilter.Resolved => ConflictEventFilter.Failed,
			_ => ConflictEventFilter.All
		};
		RefreshConflictEventPresentation();
	}

	[RelayCommand]
	private void ResetConflictLogFilters()
	{
		var changed = false;
		if (!IsConflictLogFilteredToActiveDocument)
		{
			IsConflictLogFilteredToActiveDocument = true;
			changed = true;
		}

		if (SelectedConflictEventFilter != ConflictEventFilter.All)
		{
			SelectedConflictEventFilter = ConflictEventFilter.All;
			changed = true;
		}

		if (changed)
		{
			RefreshConflictEventPresentation();
		}
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

	partial void OnRecentConflictEventsChanged(ConflictEventListItem[] value)
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

	partial void OnIsConflictLogFilteredToActiveDocumentChanged(bool value)
	{
		OnPropertyChanged(nameof(ConflictLogScopeToggleText));
		OnPropertyChanged(nameof(ConflictLogScopeText));
		OnPropertyChanged(nameof(CanResetConflictLogFilters));
		SaveConflictLogPreferences();
	}

	partial void OnAvailableConflictEventCountChanged(int value)
	{
		OnPropertyChanged(nameof(HasAnyConflictEvents));
	}

	partial void OnSelectedConflictEventFilterChanged(ConflictEventFilter value)
	{
		OnPropertyChanged(nameof(ConflictEventFilterToggleText));
		OnPropertyChanged(nameof(ConflictEventFilterText));
		OnPropertyChanged(nameof(CanResetConflictLogFilters));
		SaveConflictLogPreferences();
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
		if (_enableConflictLogPreferencePersistence)
		{
			LoadConflictLogPreferences();
		}
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
		var allEvents = _workspaceService.GetConflictEvents();
		AvailableConflictEventCount = allEvents.Count;

		var activeDocumentId = _workspaceService.GetState().ActiveDocumentId;
		var filteredEvents = allEvents.AsEnumerable();
		if (IsConflictLogFilteredToActiveDocument)
		{
			if (string.IsNullOrWhiteSpace(activeDocumentId))
			{
				filteredEvents = [];
			}
			else
			{
				filteredEvents = filteredEvents.Where(item => string.Equals(item.DocumentId, activeDocumentId, StringComparison.Ordinal));
			}
		}

		filteredEvents = filteredEvents.Where(item => IsConflictEventFilterMatch(item.Action, SelectedConflictEventFilter));

		var recentEvents = filteredEvents
			.OrderByDescending(item => item.OccurredAt)
			.Take(5)
			.ToArray();

		var includeDocumentId = !IsConflictLogFilteredToActiveDocument;

		if (recentEvents.Length == 0)
		{
			LatestConflictEventMessage = null;
			LatestConflictEventForeground = "#605E5C";
			RecentConflictEvents = [];
			RecentConflictEventMessages = [];
			IsConflictLogExpanded = false;
			return;
		}

		var latest = recentEvents[0];
		var latestCategory = GetConflictEventCategory(latest.Action);
		LatestConflictEventForeground = GetConflictEventForeground(latestCategory);
		var latestPrefix = GetConflictEventPrefix(latestCategory);
		LatestConflictEventMessage = includeDocumentId
			? $"冲突日志：{latestPrefix}[{latest.DocumentId}] {latest.Message}（{latest.OccurredAt.ToLocalTime():HH:mm:ss}）"
			: $"冲突日志：{latestPrefix}{latest.Message}（{latest.OccurredAt.ToLocalTime():HH:mm:ss}）";

		RecentConflictEvents = recentEvents
			.Select(item =>
			{
				var category = GetConflictEventCategory(item.Action);
				var prefix = GetConflictEventPrefix(category);
				var text = includeDocumentId
					? $"[{item.OccurredAt.ToLocalTime():HH:mm:ss}] {prefix}({item.DocumentId}) {item.Message}"
					: $"[{item.OccurredAt.ToLocalTime():HH:mm:ss}] {prefix}{item.Message}";
				return new ConflictEventListItem(text, GetConflictEventForeground(category));
			})
			.ToArray();

		RecentConflictEventMessages = RecentConflictEvents
			.Select(item => item.Text)
			.ToArray();
	}

	private static bool IsConflictEventFilterMatch(string action, ConflictEventFilter filter)
	{
		var category = GetConflictEventCategory(action);
		return filter switch
		{
			ConflictEventFilter.All => true,
			ConflictEventFilter.Detected => category == ConflictEventFilter.Detected,
			ConflictEventFilter.Resolved => category == ConflictEventFilter.Resolved,
			ConflictEventFilter.Failed => category == ConflictEventFilter.Failed,
			_ => true
		};
	}

	private static ConflictEventFilter GetConflictEventCategory(string action)
	{
		if (action.Contains("failed", StringComparison.OrdinalIgnoreCase))
		{
			return ConflictEventFilter.Failed;
		}

		if (action.StartsWith("resolved", StringComparison.OrdinalIgnoreCase))
		{
			return ConflictEventFilter.Resolved;
		}

		if (action.StartsWith("detected", StringComparison.OrdinalIgnoreCase))
		{
			return ConflictEventFilter.Detected;
		}

		return ConflictEventFilter.All;
	}

	private static string GetConflictEventPrefix(ConflictEventFilter category)
	{
		return category switch
		{
			ConflictEventFilter.Detected => "[检测] ",
			ConflictEventFilter.Resolved => "[处置] ",
			ConflictEventFilter.Failed => "[失败] ",
			_ => "[其他] "
		};
	}

	private static string GetConflictEventForeground(ConflictEventFilter category)
	{
		return category switch
		{
			ConflictEventFilter.Detected => "#8A6D1A",
			ConflictEventFilter.Resolved => "#107C10",
			ConflictEventFilter.Failed => "#D13438",
			_ => "#605E5C"
		};
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

	private void LoadConflictLogPreferences()
	{
		var settingsPath = GetConflictLogPreferencesPath();
		if (settingsPath is null || !File.Exists(settingsPath))
		{
			return;
		}

		try
		{
			var json = File.ReadAllText(settingsPath);
			var preferences = JsonSerializer.Deserialize<ConflictLogPreferences>(json);
			if (preferences is null)
			{
				return;
			}

			_isLoadingConflictLogPreferences = true;
			IsConflictLogFilteredToActiveDocument = preferences.IsScopeActiveDocument;
			SelectedConflictEventFilter = ParseConflictEventFilter(preferences.EventFilter);
		}
		catch
		{
			// Ignore preference loading failures to keep editor startup resilient.
		}
		finally
		{
			_isLoadingConflictLogPreferences = false;
		}

		RefreshConflictEventPresentation();
	}

	private void SaveConflictLogPreferences()
	{
		if (!_enableConflictLogPreferencePersistence || _isLoadingConflictLogPreferences)
		{
			return;
		}

		lock (_conflictLogPreferenceSaveLock)
		{
			_hasPendingConflictLogPreferenceSave = true;
			_conflictLogPreferenceSaveTimer.Change(ConflictLogPreferenceSaveDebounceMs, Timeout.Infinite);
		}
	}

	private void FlushConflictLogPreferencesSave()
	{
		if (!_enableConflictLogPreferencePersistence)
		{
			return;
		}

		var shouldWrite = false;
		var now = DateTimeOffset.UtcNow;
		lock (_conflictLogPreferenceSaveLock)
		{
			if (!_hasPendingConflictLogPreferenceSave)
			{
				return;
			}

			var elapsed = now - _lastConflictLogPreferenceWriteAt;
			if (elapsed.TotalMilliseconds < ConflictLogPreferenceSaveMinIntervalMs)
			{
				var remaining = ConflictLogPreferenceSaveMinIntervalMs - (int)Math.Max(elapsed.TotalMilliseconds, 0);
				_conflictLogPreferenceSaveTimer.Change(Math.Max(remaining, ConflictLogPreferenceSaveDebounceMs), Timeout.Infinite);
				return;
			}

			shouldWrite = true;
			_hasPendingConflictLogPreferenceSave = false;
		}

		if (!shouldWrite)
		{
			return;
		}

		var settingsPath = GetConflictLogPreferencesPath();
		if (settingsPath is null)
		{
			return;
		}

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
			var preferences = new ConflictLogPreferences(IsConflictLogFilteredToActiveDocument, SelectedConflictEventFilter.ToString());
			var json = JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true });
			File.WriteAllText(settingsPath, json);
			lock (_conflictLogPreferenceSaveLock)
			{
				_lastConflictLogPreferenceWriteAt = DateTimeOffset.UtcNow;
			}
		}
		catch
		{
			// Ignore preference persistence failures; this should not block core editing.
		}
	}

	private string? GetConflictLogPreferencesPath()
	{
		var workspaceRoot = _workspaceService.GetState().WorkspaceRoot;
		if (string.IsNullOrWhiteSpace(workspaceRoot))
		{
			return null;
		}

		var normalizedRoot = workspaceRoot.Replace('/', Path.DirectorySeparatorChar);
		return Path.Combine(normalizedRoot, MuseSettingsDirectoryName, SettingsDirectoryName, ConflictLogPreferencesFileName);
	}

	private static ConflictEventFilter ParseConflictEventFilter(string? value)
	{
		if (Enum.TryParse<ConflictEventFilter>(value, true, out var parsed))
		{
			return parsed;
		}

		return ConflictEventFilter.All;
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

public enum ConflictEventFilter
{
	All,
	Detected,
	Resolved,
	Failed
}

public sealed record ConflictEventListItem(string Text, string Foreground);

public sealed record ConflictLogPreferences(bool IsScopeActiveDocument, string EventFilter);
