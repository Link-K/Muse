using Muse.Rendering;
using Muse.ViewModels;
using Muse.Workspace;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
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
		Assert.True(viewModel.HasLatestConflictEvent);
		Assert.Contains("冲突日志", viewModel.LatestConflictEventMessage);
	}

	[Fact]
	public void ResolveConflictBySavingLocalCommand_ShouldClearConflictAndSetSuccessStatus()
	{
		var preview = new FakePreviewService();
		var state = new WorkspaceState(
			"D:/repo",
			[],
			[new WorkspaceTabState("doc-1", "D:/repo/files/a.md", true, DateTimeOffset.UtcNow) { HasExternalConflict = true, ConflictMessage = "检测到外部文件变更，当前草稿尚未同步。" }],
			"doc-1");
		var workspace = new FakeWorkspaceService(state);
		workspace.Drafts["doc-1"] = "Local draft";
		var viewModel = new MainViewModel(preview, workspace);

		viewModel.ResolveConflictBySavingLocalCommand.Execute(null);

		Assert.False(viewModel.HasActiveDocumentConflict);
		Assert.Equal("已保留本地并覆盖保存", viewModel.LastSaveStatus);
		Assert.Equal("已使用本地内容覆盖外部文件。", viewModel.SaveFeedbackMessage);
	}

	[Fact]
	public void ResolveConflictByReloadingExternalCommand_ShouldReloadDraftAndClearConflict()
	{
		var preview = new FakePreviewService();
		var state = new WorkspaceState(
			"D:/repo",
			[],
			[new WorkspaceTabState("doc-1", "D:/repo/files/a.md", true, DateTimeOffset.UtcNow) { HasExternalConflict = true, ConflictMessage = "检测到外部文件变更，当前草稿尚未同步。" }],
			"doc-1");
		var workspace = new FakeWorkspaceService(state);
		workspace.Drafts["doc-1"] = "External synced";
		var viewModel = new MainViewModel(preview, workspace);

		viewModel.ResolveConflictByReloadingExternalCommand.Execute(null);

		Assert.False(viewModel.HasActiveDocumentConflict);
		Assert.Equal("External synced", viewModel.MarkdownDraft);
		Assert.Equal("已丢弃本地并重载外部", viewModel.LastSaveStatus);
	}

	[Fact]
	public void WorkspaceChangedEvent_ShouldExposeRecentConflictEventsAndSupportExpandToggle()
	{
		var preview = new FakePreviewService();
		var state = new WorkspaceState(
			"D:/repo",
			[],
			[new WorkspaceTabState("doc-1", "D:/repo/files/a.md", true, DateTimeOffset.UtcNow)],
			"doc-1");
		var workspace = new FakeWorkspaceService(state);
		workspace.ConflictEvents.Add(new ConflictEvent("doc-1", "detected_external_change", "检测到外部文件变更，当前草稿尚未同步。", DateTimeOffset.UtcNow.AddMinutes(-6)));
		workspace.ConflictEvents.Add(new ConflictEvent("doc-1", "resolved_save_local", "已保留本地并覆盖保存外部文件。", DateTimeOffset.UtcNow.AddMinutes(-5)));
		workspace.ConflictEvents.Add(new ConflictEvent("doc-1", "detected_external_change", "检测到外部文件变更，当前草稿尚未同步。", DateTimeOffset.UtcNow.AddMinutes(-4)));
		workspace.ConflictEvents.Add(new ConflictEvent("doc-1", "resolved_reload_external", "已丢弃本地并重载外部文件内容。", DateTimeOffset.UtcNow.AddMinutes(-3)));
		workspace.ConflictEvents.Add(new ConflictEvent("doc-1", "detected_external_change", "检测到外部文件变更，当前草稿尚未同步。", DateTimeOffset.UtcNow.AddMinutes(-2)));
		workspace.ConflictEvents.Add(new ConflictEvent("doc-1", "resolved_save_local", "已保留本地并覆盖保存外部文件。", DateTimeOffset.UtcNow.AddMinutes(-1)));
		var viewModel = new MainViewModel(preview, workspace);

		workspace.RaiseWorkspaceChanged();

		Assert.True(viewModel.HasRecentConflictEvents);
		Assert.Equal(5, viewModel.RecentConflictEventMessages.Length);
		Assert.False(viewModel.ShowExpandedConflictLogPanel);

		viewModel.ToggleConflictLogExpandedCommand.Execute(null);

		Assert.True(viewModel.ShowExpandedConflictLogPanel);
		Assert.Equal("收起最近冲突日志", viewModel.ConflictLogToggleText);
	}

	[Fact]
	public void ConflictLogScopeToggle_ShouldSwitchBetweenActiveDocumentAndGlobalEvents()
	{
		var preview = new FakePreviewService();
		var state = new WorkspaceState(
			"D:/repo",
			[],
			[
				new WorkspaceTabState("doc-1", "D:/repo/files/a.md", true, DateTimeOffset.UtcNow),
				new WorkspaceTabState("doc-2", "D:/repo/files/b.md", true, DateTimeOffset.UtcNow)
			],
			"doc-1");
		var workspace = new FakeWorkspaceService(state);
		workspace.ConflictEvents.Add(new ConflictEvent("doc-1", "detected_external_change", "检测到外部文件变更，当前草稿尚未同步。", DateTimeOffset.UtcNow.AddMinutes(-2)));
		workspace.ConflictEvents.Add(new ConflictEvent("doc-2", "resolved_save_local", "已保留本地并覆盖保存外部文件。", DateTimeOffset.UtcNow.AddMinutes(-1)));
		var viewModel = new MainViewModel(preview, workspace);

		workspace.RaiseWorkspaceChanged();

		Assert.True(viewModel.HasAnyConflictEvents);
		Assert.Equal("日志范围：当前文档", viewModel.ConflictLogScopeText);
		Assert.Single(viewModel.RecentConflictEventMessages);
		Assert.DoesNotContain("(doc-2)", viewModel.RecentConflictEventMessages[0]);

		viewModel.ToggleConflictLogScopeCommand.Execute(null);

		Assert.Equal("日志范围：全部文档", viewModel.ConflictLogScopeText);
		Assert.Equal(2, viewModel.RecentConflictEventMessages.Length);
		Assert.Contains("(doc-2)", viewModel.RecentConflictEventMessages[0]);
	}

	[Fact]
	public void ConflictEventFilterCycle_ShouldFilterByTypeAndUpdateForeground()
	{
		var preview = new FakePreviewService();
		var state = new WorkspaceState(
			"D:/repo",
			[],
			[new WorkspaceTabState("doc-1", "D:/repo/files/a.md", true, DateTimeOffset.UtcNow)],
			"doc-1");
		var workspace = new FakeWorkspaceService(state);
		workspace.ConflictEvents.Add(new ConflictEvent("doc-1", "detected_external_change", "检测到外部文件变更，当前草稿尚未同步。", DateTimeOffset.UtcNow.AddMinutes(-3)));
		workspace.ConflictEvents.Add(new ConflictEvent("doc-1", "resolved_save_local", "已保留本地并覆盖保存外部文件。", DateTimeOffset.UtcNow.AddMinutes(-2)));
		workspace.ConflictEvents.Add(new ConflictEvent("doc-1", "resolve_save_local_failed", "保留本地并覆盖保存失败。", DateTimeOffset.UtcNow.AddMinutes(-1)));
		var viewModel = new MainViewModel(preview, workspace);

		workspace.RaiseWorkspaceChanged();

		Assert.Equal("事件类型：全部", viewModel.ConflictEventFilterText);
		Assert.Equal(3, viewModel.RecentConflictEventMessages.Length);
		Assert.Equal("#D13438", viewModel.LatestConflictEventForeground);

		viewModel.CycleConflictEventFilterCommand.Execute(null);

		Assert.Equal("事件类型：检测", viewModel.ConflictEventFilterText);
		Assert.Single(viewModel.RecentConflictEventMessages);
		Assert.Contains("[检测]", viewModel.RecentConflictEventMessages[0]);

		viewModel.CycleConflictEventFilterCommand.Execute(null);

		Assert.Equal("事件类型：处置", viewModel.ConflictEventFilterText);
		Assert.Single(viewModel.RecentConflictEventMessages);
		Assert.Contains("[处置]", viewModel.RecentConflictEventMessages[0]);

		viewModel.CycleConflictEventFilterCommand.Execute(null);

		Assert.Equal("事件类型：失败", viewModel.ConflictEventFilterText);
		Assert.Single(viewModel.RecentConflictEventMessages);
		Assert.Contains("[失败]", viewModel.RecentConflictEventMessages[0]);
		Assert.Equal("#D13438", viewModel.LatestConflictEventForeground);
	}

	[Fact]
	public void ResetConflictLogFiltersCommand_ShouldRestoreDefaultScopeAndFilter()
	{
		var preview = new FakePreviewService();
		var state = new WorkspaceState(
			"D:/repo",
			[],
			[
				new WorkspaceTabState("doc-1", "D:/repo/files/a.md", true, DateTimeOffset.UtcNow),
				new WorkspaceTabState("doc-2", "D:/repo/files/b.md", true, DateTimeOffset.UtcNow)
			],
			"doc-1");
		var workspace = new FakeWorkspaceService(state);
		workspace.ConflictEvents.Add(new ConflictEvent("doc-1", "detected_external_change", "检测到外部文件变更，当前草稿尚未同步。", DateTimeOffset.UtcNow.AddMinutes(-3)));
		workspace.ConflictEvents.Add(new ConflictEvent("doc-1", "resolve_save_local_failed", "保留本地并覆盖保存失败。", DateTimeOffset.UtcNow.AddMinutes(-2)));
		workspace.ConflictEvents.Add(new ConflictEvent("doc-2", "resolved_save_local", "已保留本地并覆盖保存外部文件。", DateTimeOffset.UtcNow.AddMinutes(-1)));
		var viewModel = new MainViewModel(preview, workspace);

		workspace.RaiseWorkspaceChanged();

		viewModel.ToggleConflictLogScopeCommand.Execute(null);
		viewModel.CycleConflictEventFilterCommand.Execute(null);
		viewModel.CycleConflictEventFilterCommand.Execute(null);
		viewModel.CycleConflictEventFilterCommand.Execute(null);

		Assert.Equal("日志范围：全部文档", viewModel.ConflictLogScopeText);
		Assert.Equal("事件类型：失败", viewModel.ConflictEventFilterText);
		Assert.True(viewModel.CanResetConflictLogFilters);

		workspace.RaiseWorkspaceChanged();

		Assert.Equal("日志范围：全部文档", viewModel.ConflictLogScopeText);
		Assert.Equal("事件类型：失败", viewModel.ConflictEventFilterText);

		viewModel.ResetConflictLogFiltersCommand.Execute(null);

		Assert.Equal("日志范围：当前文档", viewModel.ConflictLogScopeText);
		Assert.Equal("事件类型：全部", viewModel.ConflictEventFilterText);
		Assert.False(viewModel.CanResetConflictLogFilters);
	}

	[Fact]
	public void ConflictLogPreferences_ShouldPersistAndRestoreAcrossViewModelInstances()
	{
		var preview = new FakePreviewService();
		var tempRoot = Path.Combine(Path.GetTempPath(), "Muse-ConflictLogPrefs-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tempRoot);

		try
		{
			var state = new WorkspaceState(
				tempRoot,
				[],
				[new WorkspaceTabState("doc-1", Path.Combine(tempRoot, "files", "a.md").Replace('\\', '/'), true, DateTimeOffset.UtcNow)],
				"doc-1");

			var workspaceA = new FakeWorkspaceService(state);
			var viewModelA = new MainViewModel(preview, workspaceA, true);

			workspaceA.RaiseWorkspaceChanged();
			viewModelA.ToggleConflictLogScopeCommand.Execute(null);
			viewModelA.CycleConflictEventFilterCommand.Execute(null);
			viewModelA.CycleConflictEventFilterCommand.Execute(null);

			Assert.Equal("日志范围：全部文档", viewModelA.ConflictLogScopeText);
			Assert.Equal("事件类型：处置", viewModelA.ConflictEventFilterText);

			var settingsPath = Path.Combine(tempRoot, ".muse", "settings", "conflict-log.json");
			Assert.True(WaitForConflictLogPreferences(settingsPath, false, "Resolved"));

			var workspaceB = new FakeWorkspaceService(state);
			var viewModelB = new MainViewModel(preview, workspaceB, true);

			Assert.Equal("日志范围：全部文档", viewModelB.ConflictLogScopeText);
			Assert.Equal("事件类型：处置", viewModelB.ConflictEventFilterText);
		}
		finally
		{
			if (Directory.Exists(tempRoot))
			{
				Directory.Delete(tempRoot, true);
			}
		}
	}

	[Fact]
	public void ConflictLogPreferences_WithRapidChanges_ShouldPersistFinalStateAfterDebounce()
	{
		var preview = new FakePreviewService();
		var tempRoot = Path.Combine(Path.GetTempPath(), "Muse-ConflictLogPrefs-Debounce-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tempRoot);

		try
		{
			var state = new WorkspaceState(
				tempRoot,
				[],
				[new WorkspaceTabState("doc-1", Path.Combine(tempRoot, "files", "a.md").Replace('\\', '/'), true, DateTimeOffset.UtcNow)],
				"doc-1");

			var workspaceA = new FakeWorkspaceService(state);
			var viewModelA = new MainViewModel(preview, workspaceA, true);

			for (var i = 0; i < 6; i++)
			{
				viewModelA.CycleConflictEventFilterCommand.Execute(null);
			}
			viewModelA.ToggleConflictLogScopeCommand.Execute(null);
			viewModelA.CycleConflictEventFilterCommand.Execute(null);
			viewModelA.CycleConflictEventFilterCommand.Execute(null);

			Assert.Equal("日志范围：全部文档", viewModelA.ConflictLogScopeText);
			Assert.Equal("事件类型：全部", viewModelA.ConflictEventFilterText);

			var settingsPath = Path.Combine(tempRoot, ".muse", "settings", "conflict-log.json");
			Assert.True(WaitForConflictLogPreferences(settingsPath, false, "All"));

			var workspaceB = new FakeWorkspaceService(state);
			var viewModelB = new MainViewModel(preview, workspaceB, true);
			Assert.Equal("日志范围：全部文档", viewModelB.ConflictLogScopeText);
			Assert.Equal("事件类型：全部", viewModelB.ConflictEventFilterText);
		}
		finally
		{
			if (Directory.Exists(tempRoot))
			{
				Directory.Delete(tempRoot, true);
			}
		}
	}

	[Fact]
	public void FlushConflictLogPreferencesNow_ShouldPersistImmediatelyWithoutWaitingDebounce()
	{
		var preview = new FakePreviewService();
		var tempRoot = Path.Combine(Path.GetTempPath(), "Muse-ConflictLogPrefs-FlushNow-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tempRoot);

		try
		{
			var state = new WorkspaceState(
				tempRoot,
				[],
				[new WorkspaceTabState("doc-1", Path.Combine(tempRoot, "files", "a.md").Replace('\\', '/'), true, DateTimeOffset.UtcNow)],
				"doc-1");

			var workspaceA = new FakeWorkspaceService(state);
			var viewModelA = new MainViewModel(preview, workspaceA, true);
			viewModelA.ToggleConflictLogScopeCommand.Execute(null);
			viewModelA.CycleConflictEventFilterCommand.Execute(null);
			viewModelA.CycleConflictEventFilterCommand.Execute(null);
			viewModelA.FlushConflictLogPreferencesNow();

			var settingsPath = Path.Combine(tempRoot, ".muse", "settings", "conflict-log.json");
			Assert.True(WaitForConflictLogPreferences(settingsPath, false, "Resolved", 1000));

			var workspaceB = new FakeWorkspaceService(state);
			var viewModelB = new MainViewModel(preview, workspaceB, true);
			Assert.Equal("日志范围：全部文档", viewModelB.ConflictLogScopeText);
			Assert.Equal("事件类型：处置", viewModelB.ConflictEventFilterText);
		}
		finally
		{
			if (Directory.Exists(tempRoot))
			{
				Directory.Delete(tempRoot, true);
			}
		}
	}

	[Fact]
	public void FlushConflictLogPreferencesNow_ShouldIncreaseDebugFlushAttemptCounter()
	{
		var preview = new FakePreviewService();
		var tempRoot = Path.Combine(Path.GetTempPath(), "Muse-ConflictLogPrefs-DebugCounter-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tempRoot);

		try
		{
			var state = new WorkspaceState(
				tempRoot,
				[],
				[new WorkspaceTabState("doc-1", Path.Combine(tempRoot, "files", "a.md").Replace('\\', '/'), true, DateTimeOffset.UtcNow)],
				"doc-1");

			var workspace = new FakeWorkspaceService(state);
			var viewModel = new MainViewModel(preview, workspace, true);
			viewModel.ToggleConflictLogScopeCommand.Execute(null);
			viewModel.CycleConflictEventFilterCommand.Execute(null);
			viewModel.FlushConflictLogPreferencesNow();

			Assert.True(viewModel.DebugConflictLogFlushAttemptCount >= 1);
			Assert.Equal(0, viewModel.DebugConflictLogFlushFailureCount);
			Assert.True(string.IsNullOrWhiteSpace(viewModel.DebugLastConflictLogFlushError));
		}
		finally
		{
			if (Directory.Exists(tempRoot))
			{
				Directory.Delete(tempRoot, true);
			}
		}
	}

	[Fact]
	public void DebugTelemetryPanel_ShouldToggleAndShowFlushSummary()
	{
		var preview = new FakePreviewService();
		var tempRoot = Path.Combine(Path.GetTempPath(), "Muse-ConflictLogPrefs-DebugPanel-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tempRoot);

		try
		{
			var state = new WorkspaceState(
				tempRoot,
				[],
				[new WorkspaceTabState("doc-1", Path.Combine(tempRoot, "files", "a.md").Replace('\\', '/'), true, DateTimeOffset.UtcNow)],
				"doc-1");
			var workspace = new FakeWorkspaceService(state);
			var viewModel = new MainViewModel(preview, workspace, true);

			Assert.True(viewModel.IsDebugTelemetryAvailable);
			Assert.False(viewModel.ShowDebugTelemetryPanel);

			viewModel.ToggleDebugTelemetryExpandedCommand.Execute(null);
			viewModel.ToggleConflictLogScopeCommand.Execute(null);
			viewModel.FlushConflictLogPreferencesNow();
			viewModel.RefreshDebugTelemetryCommand.Execute(null);

			Assert.True(viewModel.ShowDebugTelemetryPanel);
			Assert.Contains("Flush 统计", viewModel.DebugConflictLogFlushSummary);
			Assert.Contains("尝试", viewModel.DebugConflictLogFlushSummary);
		}
		finally
		{
			if (Directory.Exists(tempRoot))
			{
				Directory.Delete(tempRoot, true);
			}
		}
	}

	private static bool WaitForConflictLogPreferences(string settingsPath, bool expectedScope, string expectedFilter, int timeoutMs = 5000)
	{
		var sw = Stopwatch.StartNew();
		while (sw.ElapsedMilliseconds < timeoutMs)
		{
			if (File.Exists(settingsPath))
			{
				try
				{
					using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath));
					if (doc.RootElement.TryGetProperty("IsScopeActiveDocument", out var scopeElement)
						&& doc.RootElement.TryGetProperty("EventFilter", out var filterElement)
						&& scopeElement.GetBoolean() == expectedScope
						&& string.Equals(filterElement.GetString(), expectedFilter, StringComparison.OrdinalIgnoreCase))
					{
						return true;
					}
				}
				catch
				{
					// Keep polling until timeout.
				}
			}

			Thread.Sleep(50);
		}

		return false;
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

		public List<ConflictEvent> ConflictEvents { get; } = [];

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
				var hasConflict = tab.IsDirty && draft is not null && draftContent != content;
				if (hasConflict)
				{
					ConflictEvents.Add(new ConflictEvent(tab.DocumentId, "detected_external_change", "检测到外部文件变更，当前草稿尚未同步。", DateTimeOffset.UtcNow));
				}
				tabs[i] = tab with
				{
					HasExternalConflict = hasConflict,
					ConflictMessage = hasConflict ? "检测到外部文件变更，当前草稿尚未同步。" : null
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
					tabs[i] = tabs[i] with
					{
						IsDirty = false,
						HasExternalConflict = false,
						ConflictMessage = null
					};
					_state = _state with { OpenTabs = tabs };
					Drafts[documentId] = content;
					RaiseWorkspaceChanged();
					ConflictEvents.Add(new ConflictEvent(documentId, "resolved_save_local", "已保留本地并覆盖保存外部文件。", DateTimeOffset.UtcNow));
					return SaveDocumentResult.Success(tabs[i]);
				}
			}

			return SaveDocumentResult.Failure("document_not_found", "Document was not found in current workspace tabs.");
		}

		public SaveDocumentResult ResolveConflictBySavingLocal(string documentId, string localContent)
		{
			return SaveDocument(documentId, localContent);
		}

		public SaveDocumentResult ResolveConflictByReloadingFromDisk(string documentId)
		{
			if (!Drafts.TryGetValue(documentId, out var draft))
			{
				return SaveDocumentResult.Failure("external_missing", "External file no longer exists.");
			}

			var tabs = _state.OpenTabs.ToArray();
			for (var i = 0; i < tabs.Length; i++)
			{
				if (tabs[i].DocumentId == documentId)
				{
					tabs[i] = tabs[i] with
					{
						IsDirty = false,
						HasExternalConflict = false,
						ConflictMessage = null,
						LastTouchedAt = DateTimeOffset.UtcNow
					};
					_state = _state with { OpenTabs = tabs };
					ConflictEvents.Add(new ConflictEvent(documentId, "resolved_reload_external", "已丢弃本地并重载外部文件内容。", DateTimeOffset.UtcNow));
					RaiseWorkspaceChanged();
					return new SaveDocumentResult(true, "reloaded", "Reloaded external content.", tabs[i]);
				}
			}

			return SaveDocumentResult.Failure("document_not_found", "Document was not found in current workspace tabs.");
		}

		public WorkspaceState GetState()
		{
			return _state;
		}

		public IReadOnlyList<ConflictEvent> GetConflictEvents()
		{
			return ConflictEvents;
		}

		public void RaiseWorkspaceChanged()
		{
			WorkspaceChanged?.Invoke(this, EventArgs.Empty);
		}
	}
}
