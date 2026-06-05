using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Muse.Rendering;
using Muse.Workspace;
using Muse.Editor.Rendering;

namespace Muse.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
	private readonly Muse.Services.IDialogService? _dialogService;
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

	// Exponential backoff for preference save failures
	private int _conflictLogPreferenceSaveFailureCount;
	private const int ConflictLogPrefRetryBaseMs = 2000; // 2s base
	private const int ConflictLogPrefRetryMaxMs = 30000; // 30s max

	// Countdown timer for UI display of next retry
	private DateTimeOffset? _conflictLogPreferenceNextRetryAt;
	private Timer? _conflictLogPreferenceCountdownTimer;

	// Pending saved images to inject into preview blocks (rel -> abs)
	private readonly object _pendingSavedImagesLock = new object();
	private readonly Queue<KeyValuePair<string, string>> _pendingSavedImages = new();

#if DEBUG
	private int _debugConflictLogFlushAttemptCount;
	private int _debugConflictLogFlushFailureCount;
	private string? _debugLastConflictLogFlushError;
#endif

	public MainViewModel()
		: this(new MarkdownPreviewService(), new InMemoryWorkspaceService(enableBackgroundAutoSave: true), true)
	{
	}

	internal MainViewModel(IMarkdownPreviewService previewService, IWorkspaceService workspaceService)
		: this(previewService, workspaceService, false)
	{
	}

	internal MainViewModel(IMarkdownPreviewService previewService, IWorkspaceService workspaceService, bool enableConflictLogPreferencePersistence, Muse.Services.IDialogService? dialogService = null)
	{
		_previewService = previewService;
		_workspaceService = workspaceService;
		_dialogService = dialogService;
		_enableConflictLogPreferencePersistence = enableConflictLogPreferencePersistence;
		_conflictLogPreferenceSaveTimer = new Timer(_ => FlushConflictLogPreferencesSave(false), null, Timeout.Infinite, Timeout.Infinite);
		_workspaceService.WorkspaceChanged += HandleWorkspaceChanged;
		LoadWorkspace(Environment.CurrentDirectory);
		TryOpenDefaultTaskDocument();
		RefreshPreview();
	}

	public void Dispose()
	{
		FlushConflictLogPreferencesNow();
		_conflictLogPreferenceSaveTimer.Dispose();
		_conflictLogPreferenceCountdownTimer?.Dispose();
		_workspaceService.WorkspaceChanged -= HandleWorkspaceChanged;
	}

	internal int DebugConflictLogFlushAttemptCount
	{
		get
		{
#if DEBUG
			return _debugConflictLogFlushAttemptCount;
#else
			return 0;
#endif
		}
	}

	internal int DebugConflictLogFlushFailureCount
	{
		get
		{
#if DEBUG
			return _debugConflictLogFlushFailureCount;
#else
			return 0;
#endif
		}
	}

	internal string? DebugLastConflictLogFlushError
	{
		get
		{
#if DEBUG
			return _debugLastConflictLogFlushError;
#else
			return null;
#endif
		}
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
	private PreviewBlockViewModel[] _previewBlocks = Array.Empty<PreviewBlockViewModel>();

	[ObservableProperty]
	private string? _previewDiagnostic;

	[ObservableProperty]
	private string _workspaceRootDisplay = "未加载";

	[ObservableProperty]
	private FileTreeNodeViewModel[] _fileTree = Array.Empty<FileTreeNodeViewModel>();

	[ObservableProperty]
	private WorkspaceTabViewModel[] _workspaceTabs = Array.Empty<WorkspaceTabViewModel>();

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

	[ObservableProperty]
	private bool _isDebugTelemetryExpanded;

	[ObservableProperty]
	private string? _conflictLogPreferenceSaveErrorMessage;

	[ObservableProperty]
	private bool _isConflictLogPreferenceSaveErrorExpanded;

	[ObservableProperty]
	private int? _conflictLogPreferenceNextRetrySeconds;

	[ObservableProperty]
	private string? _debugExportDirectory;

	public bool HasConflictLogPreferenceNextRetry => ConflictLogPreferenceNextRetrySeconds.HasValue && ConflictLogPreferenceNextRetrySeconds.Value > 0;

	public string DebugExportDirectorySummary => string.IsNullOrWhiteSpace(DebugExportDirectory)
		? "调试文件将写入当前工作区 .muse/debug/error-copy.txt"
		: $"调试文件将写入：{DebugExportDirectory}";

	partial void OnConflictLogPreferenceNextRetrySecondsChanged(int? value)
	{
		OnPropertyChanged(nameof(HasConflictLogPreferenceNextRetry));
	}



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

	public bool HasConflictLogPreferenceSaveError => !string.IsNullOrWhiteSpace(ConflictLogPreferenceSaveErrorMessage);

	public bool IsDebugTelemetryAvailable
	{
		get
		{
#if DEBUG
			return true;
#else
			return false;
#endif
		}
	}

	public string ConflictLogPreferenceErrorToggleText => IsConflictLogPreferenceSaveErrorExpanded ? "隐藏错误详情" : "显示错误详情";


	public string DebugTelemetryToggleText => IsDebugTelemetryExpanded ? "收起调试诊断" : "展开调试诊断";

	public bool ShowDebugTelemetryPanel => IsDebugTelemetryExpanded && IsDebugTelemetryAvailable;

	public string DebugConflictLogFlushSummary => $"Flush 统计：尝试 {DebugConflictLogFlushAttemptCount} 次，失败 {DebugConflictLogFlushFailureCount} 次，最后错误：{DebugLastConflictLogFlushError ?? "无"}";

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

	// Public helper for Views to request loading a workspace path (keeps LoadWorkspace private for tests).
	public void OpenWorkspaceAt(string path)
	{
		LoadWorkspace(path);
	}

	[RelayCommand]
	private void OpenFileNode(FileTreeNode node)
	{
		if (node == null || node.IsDirectory)
		{
			return;
		}

		// 先进行轻量文件格式检查，避免加载不支持的二进制/未知格式导致界面卡死
		if (!IsFileSupported(node.Path, out var reason))
		{
			SaveFeedbackIsError = true;
			SaveFeedbackMessage = reason ?? "不支持的文件格式。";
			// 请求视图层显示模态提示框（视图可订阅此事件）
			_ = (_dialogService?.ShowMessageAsync("无法打开文件", SaveFeedbackMessage) ?? ShowUnsupportedFileDialogRequested?.Invoke("无法打开文件", SaveFeedbackMessage) ?? System.Threading.Tasks.Task.CompletedTask);
			return;
		}

		var openResult = _workspaceService.OpenDocument(node.Path);
		if (!openResult.Succeeded)
		{
			SaveFeedbackIsError = true;
			SaveFeedbackMessage = openResult.Message ?? "无法打开文件。";
			_ = (_dialogService?.ShowMessageAsync("无法打开文件", SaveFeedbackMessage) ?? ShowUnsupportedFileDialogRequested?.Invoke("无法打开文件", SaveFeedbackMessage) ?? System.Threading.Tasks.Task.CompletedTask);
			return;
		}

		SyncWorkspaceState();
		_ = LoadActiveDocumentContentAsync();
	}

	/// <summary>
	/// 当需要在视图中显示不支持格式的模态对话框时触发。
	/// 参数：标题，消息文本。
	/// </summary>
	public event Func<string, string, System.Threading.Tasks.Task>? ShowUnsupportedFileDialogRequested;

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

		// 如果扩展名未知，做一次轻量探测：读取文件前几 KB，若包含 NUL 字节则视为二进制（不支持）
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
			// otherwise treat as supported text-like
			return true;
		}
		catch (Exception ex)
		{
			reason = $"打开文件失败：{ex.Message}";
			return false;
		}
	}

	[RelayCommand]
	private void ActivateTab(string documentId)
	{
		// Fire-and-forget to keep UI responsive; real work happens in ActivateTabInternal
		if (string.IsNullOrWhiteSpace(documentId)) return;
		_ = ActivateTabInternal(documentId);
	}

	private async System.Threading.Tasks.Task ActivateTabInternal(string documentId)
	{
		if (string.IsNullOrWhiteSpace(documentId)) return;
		_workspaceService.ActivateDocument(documentId);
		SyncWorkspaceState();
		await LoadActiveDocumentContentAsync().ConfigureAwait(false);
	}

	[RelayCommand]
	private void CloseTab(string documentId)
	{
		if (string.IsNullOrWhiteSpace(documentId)) return;
		if (_workspaceService.CloseDocument(documentId))
		{
			SyncWorkspaceState();
			_ = LoadActiveDocumentContentAsync();
		}
	}

	// Reorder an open tab to a new index and refresh view state.
	public void ReorderTabs(string documentId, int newIndex)
	{
		if (string.IsNullOrWhiteSpace(documentId)) return;
		if (_workspaceService.MoveTab(documentId, newIndex))
		{
			SyncWorkspaceState();
		}
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

	[RelayCommand]
	private void ToggleDebugTelemetryExpanded()
	{
		IsDebugTelemetryExpanded = !IsDebugTelemetryExpanded;
	}

	[RelayCommand]
	private void RefreshDebugTelemetry()
	{
		OnPropertyChanged(nameof(DebugConflictLogFlushSummary));
	}

	[RelayCommand]
	private void RetryConflictLogPreferenceSave()
	{
		if (!_enableConflictLogPreferencePersistence)
		{
			return;
		}

		lock (_conflictLogPreferenceSaveLock)
		{
			_hasPendingConflictLogPreferenceSave = true;
		}

		FlushConflictLogPreferencesNow();
	}

	[RelayCommand]
	private void CancelConflictLogPreferenceRetry()
	{
		lock (_conflictLogPreferenceSaveLock)
		{
			_conflictLogPreferenceSaveTimer.Change(Timeout.Infinite, Timeout.Infinite);
			_conflictLogPreferenceSaveFailureCount = 0;
			ConflictLogPreferenceNextRetrySeconds = null;
		}
		ClearConflictLogPreferenceSaveError();
	}

	[RelayCommand]
	private void ToggleConflictLogPreferenceSaveErrorExpanded()
	{
		IsConflictLogPreferenceSaveErrorExpanded = !IsConflictLogPreferenceSaveErrorExpanded;
		OnPropertyChanged(nameof(ConflictLogPreferenceErrorToggleText));
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

	partial void OnIsDebugTelemetryExpandedChanged(bool value)
	{
		OnPropertyChanged(nameof(DebugTelemetryToggleText));
		OnPropertyChanged(nameof(ShowDebugTelemetryPanel));
	}

	partial void OnIsConflictLogPreferenceSaveErrorExpandedChanged(bool value)
	{
		OnPropertyChanged(nameof(ConflictLogPreferenceErrorToggleText));
	}

	partial void OnConflictLogPreferenceSaveErrorMessageChanged(string? value)
	{
		OnPropertyChanged(nameof(HasConflictLogPreferenceSaveError));
		if (!string.IsNullOrWhiteSpace(value))
		{
			IsConflictLogPreferenceSaveErrorExpanded = true;
			OnPropertyChanged(nameof(ConflictLogPreferenceErrorToggleText));
		}
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

	partial void OnDebugExportDirectoryChanged(string? value)
	{
		var normalized = NormalizeDebugExportDirectory(value);
		if (!string.Equals(normalized, value, StringComparison.Ordinal))
		{
			DebugExportDirectory = normalized;
			return;
		}

		OnPropertyChanged(nameof(DebugExportDirectorySummary));
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

		// Map rendering blocks into view models for per-block templating in the view.
		PreviewBlocks = BuildPreviewBlocks(viewState.Blocks);
	}

	private PreviewBlockViewModel[] BuildPreviewBlocks(IReadOnlyList<RenderedBlock>? blocks)
	{
		if (blocks is null || blocks.Count == 0)
		{
			return Array.Empty<PreviewBlockViewModel>();
		}

		var previewBlocks = blocks.Select(b => new PreviewBlockViewModel(b)).ToArray();
		// Log created VM ids for debug correlation
		try
		{
			var writer = App.Resolve<Muse.Services.IFileDebugWriter>();
			var ids = string.Join(",", previewBlocks.Select(p => p.VmId));
			_ = writer?.WriteDebugFileAsync($"[DEBUG] BuildPreviewBlocks created VMs: {ids}");
			// Also log mapping of LineNumber -> VmId for easier correlation with SetImagePath logs
			try
			{
				var mappings = string.Join(",", previewBlocks.Select(p => $"{p.LineNumber}:{p.VmId}"));
				_ = writer?.WriteDebugFileAsync($"[DEBUG] BuildPreviewBlocks mappings (Line:VmId): {mappings}");
			}
			catch { }
		}
		catch { }
		// If there are any pending saved images registered by the view (paste/drop), inject them into newly created VMs now.
		try
		{
			KeyValuePair<string, string>[] pendingSnapshot;
			lock (_pendingSavedImagesLock)
			{
				pendingSnapshot = _pendingSavedImages.ToArray();
			}
			if (pendingSnapshot is not null && pendingSnapshot.Length > 0)
			{
				foreach (var p in pendingSnapshot)
				{
					var rel = p.Key;
					var abs = p.Value;
					// find a preview block whose content contains the relative path
					var match = previewBlocks.FirstOrDefault(pb => !string.IsNullOrWhiteSpace(pb.Content) && pb.Content.Contains(rel, StringComparison.OrdinalIgnoreCase));
					if (match is not null)
					{
						try
						{
							match.AssignImagePath(abs);
							var writer2 = App.Resolve<Muse.Services.IFileDebugWriter>();
							_ = writer2?.WriteDebugFileAsync($"[DEBUG] Injected pending saved image rel={rel} abs={abs} into vm={match.VmId}");
						}
						catch { }
						// remove matched pending entry
						try
						{
							lock (_pendingSavedImagesLock)
							{
								var list = _pendingSavedImages.ToList();
								var item = list.FirstOrDefault(k => string.Equals(k.Key, rel, StringComparison.OrdinalIgnoreCase) && string.Equals(k.Value, abs, StringComparison.OrdinalIgnoreCase));
								if (!item.Equals(default(KeyValuePair<string, string>)))
								{
									list.Remove(item);
									_pendingSavedImages.Clear();
									foreach (var kv in list) _pendingSavedImages.Enqueue(kv);
								}
							}
						}
						catch { }
					}
				}
			}
		}
		catch { }
		ApplyAlignedTableText(previewBlocks);
		return previewBlocks;
	}

	private static void ApplyAlignedTableText(PreviewBlockViewModel[] previewBlocks)
	{
		for (var i = 0; i < previewBlocks.Length;)
		{
			if (!previewBlocks[i].IsTableRow)
			{
				i++;
				continue;
			}

			var start = i;
			while (i < previewBlocks.Length && previewBlocks[i].IsTableRow)
			{
				i++;
			}

			var endExclusive = i;
			ApplyAlignedTableTextForSegment(previewBlocks, start, endExclusive);
		}
	}

	private static void ApplyAlignedTableTextForSegment(PreviewBlockViewModel[] previewBlocks, int startInclusive, int endExclusive)
	{
		var maxColumns = 0;
		for (var i = startInclusive; i < endExclusive; i++)
		{
			if (!previewBlocks[i].ShowTableCells)
			{
				continue;
			}

			maxColumns = Math.Max(maxColumns, previewBlocks[i].TableCells.Length);
		}

		if (maxColumns == 0)
		{
			return;
		}

		var columnWidths = new int[maxColumns];
		for (var i = startInclusive; i < endExclusive; i++)
		{
			if (!previewBlocks[i].ShowTableCells)
			{
				continue;
			}

			for (var col = 0; col < maxColumns; col++)
			{
				var cell = col < previewBlocks[i].TableCells.Length ? previewBlocks[i].TableCells[col] : string.Empty;
				columnWidths[col] = Math.Max(columnWidths[col], cell.Length);
			}
		}

		for (var i = startInclusive; i < endExclusive; i++)
		{
			if (!previewBlocks[i].ShowTableCells)
			{
				continue;
			}

			previewBlocks[i].SetAlignedTableDisplayText(BuildAlignedTableRow(previewBlocks[i].TableCells, columnWidths));
		}

		// Collapse one contiguous table segment into a single visual block.
		var lines = new List<string>(endExclusive - startInclusive);
		var tableRows = new List<PreviewTableRowViewModel>(endExclusive - startInclusive);
		var seenDivider = false;
		for (var i = startInclusive; i < endExclusive; i++)
		{
			if (previewBlocks[i].IsTableDivider)
			{
				lines.Add(BuildDividerRow(columnWidths));
				tableRows.Add(new PreviewTableRowViewModel(Array.Empty<string>(), true, false));
				seenDivider = true;
				continue;
			}

			lines.Add(previewBlocks[i].TableDisplayText);
			var normalizedCells = NormalizeCells(previewBlocks[i].TableCells, maxColumns);
			tableRows.Add(new PreviewTableRowViewModel(normalizedCells, false, !seenDivider));
		}

		if (lines.Count > 0)
		{
			previewBlocks[startInclusive].SetAlignedTableDisplayText(string.Join("\n", lines));
			previewBlocks[startInclusive].SetTableRows(tableRows.ToArray());
			for (var i = startInclusive + 1; i < endExclusive; i++)
			{
				previewBlocks[i].SuppressRendering();
			}
		}
	}

	private static string BuildAlignedTableRow(string[] cells, int[] columnWidths)
	{
		var normalized = new string[columnWidths.Length];
		for (var i = 0; i < columnWidths.Length; i++)
		{
			var cell = i < cells.Length ? cells[i] : string.Empty;
			normalized[i] = cell.PadRight(columnWidths[i]);
		}

		return $"| {string.Join(" | ", normalized)} |";
	}

	private static string BuildDividerRow(int[] columnWidths)
	{
		var parts = columnWidths
			.Select(static width => new string('-', Math.Max(3, width)))
			.ToArray();

		return $"| {string.Join(" | ", parts)} |";
	}

	private static string[] NormalizeCells(string[] cells, int columnCount)
	{
		var normalized = new string[columnCount];
		for (var i = 0; i < columnCount; i++)
		{
			normalized[i] = i < cells.Length ? cells[i] : string.Empty;
		}

		return normalized;
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

	/// <summary>
	/// Register a recently saved image (relative path and absolute path) so that
	/// it can be injected into PreviewBlockViewModel instances when the preview is rebuilt.
	/// </summary>
	public void RegisterPendingSavedImage(string relativePath, string absolutePath)
	{
		if (string.IsNullOrWhiteSpace(relativePath) || string.IsNullOrWhiteSpace(absolutePath)) return;
		lock (_pendingSavedImagesLock)
		{
			// avoid duplicates
			if (!_pendingSavedImages.Any(k => string.Equals(k.Key, relativePath, StringComparison.OrdinalIgnoreCase) && string.Equals(k.Value, absolutePath, StringComparison.OrdinalIgnoreCase)))
			{
				_pendingSavedImages.Enqueue(new KeyValuePair<string, string>(relativePath, absolutePath));
			}
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

		var sprintResult = _workspaceService.OpenDocument(sprintTaskPath);
		if (!sprintResult.Succeeded)
		{
			SaveFeedbackIsError = true;
			SaveFeedbackMessage = sprintResult.Message ?? "无法打开任务文件。";
			_ = (_dialogService?.ShowMessageAsync("无法打开文件", SaveFeedbackMessage) ?? ShowUnsupportedFileDialogRequested?.Invoke("无法打开文件", SaveFeedbackMessage) ?? System.Threading.Tasks.Task.CompletedTask);
			return;
		}

		SyncWorkspaceState();
		_ = LoadActiveDocumentContentAsync();
	}

	private async System.Threading.Tasks.Task LoadActiveDocumentContentAsync()
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
				// assign on current context
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
			// Use asynchronous IO to avoid blocking UI thread when switching tabs
			var content = await System.IO.File.ReadAllTextAsync(filePath).ConfigureAwait(false);
			// marshal back to original context by setting property (await will capture context by default)
			MarkdownDraft = content;
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

		// Preserve expanded state from previous view models so tree doesn't auto-collapse on refresh
		var previouslyExpanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (FileTree != null)
		{
			foreach (var ft in FileTree)
			{
				CollectExpandedPaths(ft, previouslyExpanded);
			}
		}

		// Expose file tree and open tabs for UI
		FileTree = (state.FileTree is null) ? Array.Empty<FileTreeNodeViewModel>() : CreateTreeViewModels(state.FileTree, previouslyExpanded);
		// Create WorkspaceTabViewModel instances and wire per-tab Activate/Close commands to avoid DataTemplate ElementName bindings
		WorkspaceTabs = state.OpenTabs.Select(t =>
		{
			var docId = t.DocumentId;
			var activate = new ActionCommand(() => _ = ActivateTabInternal(docId));
			var close = new ActionCommand(() => CloseTab(docId));
			return new WorkspaceTabViewModel(t, t.DocumentId == state.ActiveDocumentId, activate, close);
		}).ToArray();

		OnPropertyChanged(nameof(CanSaveActiveDocument));
		OnPropertyChanged(nameof(ActiveDocumentConflictText));
		OnPropertyChanged(nameof(HasActiveDocumentConflict));
		OnPropertyChanged(nameof(WorkspaceSummary));
	}

	private FileTreeNodeViewModel[] CreateTreeViewModels(IReadOnlyList<Muse.Workspace.FileTreeNode> nodes, HashSet<string>? expandedPaths = null)
	{
		var list = new List<FileTreeNodeViewModel>(nodes.Count);
		foreach (var n in nodes)
		{
			var vm = new FileTreeNodeViewModel(n.Path, n.Name, n.IsDirectory, OpenFileNodeFromViewModel);
			// restore expanded state if previously recorded
			if (expandedPaths != null && expandedPaths.Contains(n.Path))
			{
				vm.IsExpanded = true;
			}
			if (n.Children != null && n.Children.Count > 0)
			{
				// recursively add children
				foreach (var c in n.Children)
				{
					var childVms = CreateTreeViewModels(new[] { c }, expandedPaths);
					foreach (var cv in childVms)
					{
						vm.Children.Add(cv);
					}
				}
			}
			list.Add(vm);
		}
		return list.ToArray();
	}

	private void CollectExpandedPaths(FileTreeNodeViewModel vm, HashSet<string> dest)
	{
		if (vm.IsDirectory && vm.IsExpanded)
		{
			dest.Add(vm.Path);
		}
		foreach (var c in vm.Children)
		{
			CollectExpandedPaths(c, dest);
		}
	}

	private void OpenFileNodeFromViewModel(FileTreeNodeViewModel vm)
	{
		if (vm == null || vm.IsDirectory) return;
		// If the file looks like an image, open with system default viewer instead of opening as text document
		try
		{
			var ext = Path.GetExtension(vm.Path)?.ToLowerInvariant();
			var imageExts = new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tiff", ".ico" };
			if (!string.IsNullOrWhiteSpace(ext) && imageExts.Contains(ext))
			{
				try
				{
					var psi = new ProcessStartInfo
					{
						FileName = vm.Path,
						UseShellExecute = true
					};
					Process.Start(psi);
					return;
				}
				catch (Exception ex)
				{
					SaveFeedbackIsError = true;
					SaveFeedbackMessage = $"打开图片失败：{ex.Message}";
					return;
				}
			}
		}
		catch { }

		var openResult = _workspaceService.OpenDocument(vm.Path);
		if (!openResult.Succeeded)
		{
			SaveFeedbackIsError = true;
			SaveFeedbackMessage = openResult.Message ?? "无法打开文件。";
			_ = (_dialogService?.ShowMessageAsync("无法打开文件", SaveFeedbackMessage) ?? ShowUnsupportedFileDialogRequested?.Invoke("无法打开文件", SaveFeedbackMessage) ?? System.Threading.Tasks.Task.CompletedTask);
			return;
		}

		SyncWorkspaceState();
		_ = LoadActiveDocumentContentAsync();
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
			DebugExportDirectory = NormalizeDebugExportDirectory(preferences.DebugExportDirectory);
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

	internal void FlushConflictLogPreferencesNow()
	{
		if (!_enableConflictLogPreferencePersistence)
		{
			WriteDebugLog("[ConflictLogPref] FlushNow skipped: persistence disabled.");
			return;
		}

		lock (_conflictLogPreferenceSaveLock)
		{
			_conflictLogPreferenceSaveTimer.Change(Timeout.Infinite, Timeout.Infinite);
		}

		WriteDebugLog("[ConflictLogPref] FlushNow requested.");
		FlushConflictLogPreferencesSave(true);
	}

	private void FlushConflictLogPreferencesSave(bool force)
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
				WriteDebugLog($"[ConflictLogPref] Flush skipped: no pending writes (force={force}).");
				return;
			}

			var elapsed = now - _lastConflictLogPreferenceWriteAt;
			if (!force && elapsed.TotalMilliseconds < ConflictLogPreferenceSaveMinIntervalMs)
			{
				var remaining = ConflictLogPreferenceSaveMinIntervalMs - (int)Math.Max(elapsed.TotalMilliseconds, 0);
				_conflictLogPreferenceSaveTimer.Change(Math.Max(remaining, ConflictLogPreferenceSaveDebounceMs), Timeout.Infinite);
				WriteDebugLog($"[ConflictLogPref] Flush deferred for {remaining}ms due to min interval.");
				return;
			}

			shouldWrite = true;
			_hasPendingConflictLogPreferenceSave = false;
		}

		if (!shouldWrite)
		{
			return;
		}

#if DEBUG
		Interlocked.Increment(ref _debugConflictLogFlushAttemptCount);
#endif

		var settingsPath = GetConflictLogPreferencesPath();
		if (settingsPath is null)
		{
			WriteDebugLog("[ConflictLogPref] Flush skipped: settings path unavailable.");
			return;
		}

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
			var preferences = new ConflictLogPreferences(IsConflictLogFilteredToActiveDocument, SelectedConflictEventFilter.ToString(), NormalizeDebugExportDirectory(DebugExportDirectory));
			var json = JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true });
			File.WriteAllText(settingsPath, json);
			lock (_conflictLogPreferenceSaveLock)
			{
				_lastConflictLogPreferenceWriteAt = DateTimeOffset.UtcNow;
			}
			// reset failure tracking on success
			_conflictLogPreferenceSaveFailureCount = 0;
			ConflictLogPreferenceNextRetrySeconds = null;
			ClearConflictLogPreferenceSaveError();
			WriteDebugLog($"[ConflictLogPref] Flush success (force={force}) -> {settingsPath}");
		}
		catch (Exception ex)
		{

#if DEBUG
			Interlocked.Increment(ref _debugConflictLogFlushFailureCount);
			_debugLastConflictLogFlushError = ex.Message;
#endif
			SetConflictLogPreferenceSaveError($"偏好保存失败：{ex.Message}");
			WriteDebugLog($"[ConflictLogPref] Flush failed: {ex.Message}");
			// Schedule retry with exponential backoff so we don't hammer writes on persistent failures.
			try
			{
				_conflictLogPreferenceSaveFailureCount++;
				var multiplier = Math.Min((long)1 << (_conflictLogPreferenceSaveFailureCount - 1), (long)int.MaxValue);
				var delay = (int)Math.Min((long)ConflictLogPrefRetryBaseMs * multiplier, ConflictLogPrefRetryMaxMs);
				_conflictLogPreferenceSaveTimer.Change(delay, Timeout.Infinite);
				// compute next retry absolute time and expose seconds remaining to UI (rounded up)
				_conflictLogPreferenceNextRetryAt = DateTimeOffset.UtcNow.AddMilliseconds(delay);
				ConflictLogPreferenceNextRetrySeconds = (int)Math.Max(0, Math.Ceiling(((_conflictLogPreferenceNextRetryAt.Value - DateTimeOffset.UtcNow).TotalSeconds)));
				StartConflictLogPreferenceCountdown();
				WriteDebugLog($"[ConflictLogPref] Scheduled retry in {delay}ms (failureCount={_conflictLogPreferenceSaveFailureCount}). NextRetrySeconds={ConflictLogPreferenceNextRetrySeconds}");
			}
			catch
			{
				// ignore scheduling failures
			}
			// Ignore preference persistence failures; this should not block core editing.
		}
	}

	private void SetConflictLogPreferenceSaveError(string message)
	{
		ConflictLogPreferenceSaveErrorMessage = message;
	}

	private void ClearConflictLogPreferenceSaveError()
	{
		ConflictLogPreferenceSaveErrorMessage = null;
	}


	private void StartConflictLogPreferenceCountdown()
	{
		try
		{
			if (_conflictLogPreferenceNextRetryAt is null)
			{
				return;
			}

			_conflictLogPreferenceCountdownTimer ??= new Timer(_ =>
			{
				try
				{
					if (_conflictLogPreferenceNextRetryAt is null)
					{
						ConflictLogPreferenceNextRetrySeconds = null;
						_conflictLogPreferenceCountdownTimer?.Change(Timeout.Infinite, Timeout.Infinite);
						return;
					}
					var remaining = (int)Math.Ceiling((_conflictLogPreferenceNextRetryAt.Value - DateTimeOffset.UtcNow).TotalSeconds);
					if (remaining <= 0)
					{
						ConflictLogPreferenceNextRetrySeconds = 0;
						// stop countdown; the scheduled save timer will run shortly
						_conflictLogPreferenceCountdownTimer?.Change(Timeout.Infinite, Timeout.Infinite);
						return;
					}
					ConflictLogPreferenceNextRetrySeconds = remaining;
				}
				catch { }
			}, null, 0, 1000);
		}
		catch { }
	}

	[Conditional("DEBUG")]
	private static void WriteDebugLog(string message)
	{
		Debug.WriteLine(message);
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

	private static string? NormalizeDebugExportDirectory(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}

		return value.Trim();
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

public sealed record ConflictLogPreferences(bool IsScopeActiveDocument, string EventFilter, string? DebugExportDirectory = null);
