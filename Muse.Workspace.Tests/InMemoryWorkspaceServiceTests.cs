using Xunit;

namespace Muse.Workspace.Tests;

public sealed class InMemoryWorkspaceServiceTests
{
	[Fact]
	public void OpenWorkspace_ShouldBuildTreeAndResetTabs()
	{
		var root = CreateWorkspaceFixture();
		try
		{
			var service = new InMemoryWorkspaceService();
			_ = service.OpenDocument(Path.Combine(root, "README.md"));

			var state = service.OpenWorkspace(root);

			Assert.NotNull(state.WorkspaceRoot);
			Assert.Single(state.FileTree);
			Assert.Empty(state.OpenTabs);
			Assert.Null(state.ActiveDocumentId);
			Assert.Contains(state.FileTree[0].Children, node => !node.IsDirectory && node.Name == "README.md");
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void MoveTab_PersistsOrder_AndOpenWorkspaceRestoresOrder()
	{
		var root = CreateWorkspaceFixture();
		try
		{
			var service = new InMemoryWorkspaceService();
			service.OpenWorkspace(root);
			var tabARes = service.OpenDocument(Path.Combine(root, "README.md"));
			var tabA = tabARes.Tab!;
			var tabBRes = service.OpenDocument(Path.Combine(root, "notes.md"));
			var tabB = tabBRes.Tab!;

			var moved = service.MoveTab(tabB.DocumentId, 0);
			Assert.True(moved);

			var reloaded = new InMemoryWorkspaceService();
			var state = reloaded.OpenWorkspace(root);

			Assert.Equal(2, state.OpenTabs.Count);
			Assert.Equal(tabB.DocumentId, state.OpenTabs[0].DocumentId);
			Assert.Equal(tabA.DocumentId, state.OpenTabs[1].DocumentId);
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void MoveTab_ShouldReorderTabs()
	{
		var root = CreateWorkspaceFixture();
		try
		{
			var service = new InMemoryWorkspaceService();
			service.OpenWorkspace(root);
			var tabARes = service.OpenDocument(Path.Combine(root, "README.md"));
			var tabA = tabARes.Tab!;
			var tabBRes = service.OpenDocument(Path.Combine(root, "notes.md"));
			var tabB = tabBRes.Tab!;

			var moved = service.MoveTab(tabB.DocumentId, 0);
			var state = service.GetState();

			Assert.True(moved);
			Assert.Equal(2, state.OpenTabs.Count);
			Assert.Equal(tabB.DocumentId, state.OpenTabs[0].DocumentId);
			Assert.Equal(tabA.DocumentId, state.OpenTabs[1].DocumentId);
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void MoveTab_InvalidDocument_ShouldReturnFalse()
	{
		var service = new InMemoryWorkspaceService();
		var root = CreateWorkspaceFixture();
		try
		{
			service.OpenWorkspace(root);
			var result = service.MoveTab(Path.Combine(root, "missing.md"), 0);
			Assert.False(result);
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void CloseDocument_ShouldAddToRecentlyClosed()
	{
		var root = CreateWorkspaceFixture();
		try
		{
			var service = new InMemoryWorkspaceService();
			service.OpenWorkspace(root);
			var opened = service.OpenDocument(Path.Combine(root, "README.md"));
			Assert.True(opened.Succeeded);

			var closed = service.CloseDocument(opened.Tab!.DocumentId);
			Assert.True(closed);

			var recentlyClosed = service.GetRecentlyClosed();
			Assert.Single(recentlyClosed);
			Assert.Equal(opened.Tab.DocumentId, recentlyClosed[0].FilePath);
			Assert.Equal("README.md", recentlyClosed[0].FileName);
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void OpenAndActivateDocument_ShouldSwitchActiveTab()
	{
		var root = CreateWorkspaceFixture();
		try
		{
			var service = new InMemoryWorkspaceService();
			service.OpenWorkspace(root);
			var tabARes = service.OpenDocument(Path.Combine(root, "README.md"));
			var tabA = tabARes.Tab!;
			var tabBRes = service.OpenDocument(Path.Combine(root, "notes.md"));
			var tabB = tabBRes.Tab!;

			var activated = service.ActivateDocument(tabA.DocumentId);
			var state = service.GetState();

			Assert.NotNull(activated);
			Assert.Equal(tabA.DocumentId, state.ActiveDocumentId);
			Assert.Equal(2, state.OpenTabs.Count);
			Assert.Contains(state.OpenTabs, tab => tab.DocumentId == tabB.DocumentId);
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void MarkDirty_ShouldScheduleAutoSave()
	{
		var scheduler = new MemoryAutoSaveScheduler();
		var service = new InMemoryWorkspaceService(scheduler);
		var root = CreateWorkspaceFixture();
		try
		{
			service.OpenWorkspace(root);
			var tabRes = service.OpenDocument(Path.Combine(root, "README.md"));
			var tab = tabRes.Tab!;

			var dirtyTab = service.MarkDirty(tab.DocumentId, true);
			var scheduled = scheduler.DrainScheduled();

			Assert.NotNull(dirtyTab);
			Assert.True(dirtyTab!.IsDirty);
			Assert.Single(scheduled);
			Assert.Equal(tab.DocumentId, scheduled[0]);
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void SaveDocument_ShouldClearDirtyFlag()
	{
		var service = new InMemoryWorkspaceService();
		var root = CreateWorkspaceFixture();
		try
		{
			service.OpenWorkspace(root);
			var tabRes = service.OpenDocument(Path.Combine(root, "README.md"));
			var tab = tabRes.Tab!;
			service.MarkDirty(tab.DocumentId, true);

			var saved = service.SaveDocument(tab.DocumentId, "# Updated");
			var state = service.GetState();

			Assert.True(saved.Succeeded);
			Assert.Equal("saved", saved.Code);
			Assert.NotNull(saved.Tab);
			Assert.False(saved.Tab!.IsDirty);
			Assert.Equal("# Updated", File.ReadAllText(Path.Combine(root, "README.md")));
			Assert.Contains(state.OpenTabs, item => item.DocumentId == tab.DocumentId && item.IsDirty == false);
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void SaveDocument_WhenDocumentMissing_ShouldReturnFailure()
	{
		var service = new InMemoryWorkspaceService();
		var root = CreateWorkspaceFixture();
		try
		{
			service.OpenWorkspace(root);

			var result = service.SaveDocument(Path.Combine(root, "missing.md"), "content");

			Assert.False(result.Succeeded);
			Assert.Equal("document_not_found", result.Code);
			Assert.Null(result.Tab);
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void SaveDocument_WhenDirectoryMissing_ShouldReturnIoError()
	{
		var service = new InMemoryWorkspaceService();
		var root = CreateWorkspaceFixture();
		try
		{
			service.OpenWorkspace(root);
			var tabRes = service.OpenDocument(Path.Combine(root, "missing-dir", "new.md"));
			var tab = tabRes.Tab!;
			service.MarkDirty(tab.DocumentId, true);

			var result = service.SaveDocument(tab.DocumentId, "content");

			Assert.False(result.Succeeded);
			Assert.Equal("io_error", result.Code);
			Assert.Null(result.Tab);
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void OpenWorkspace_ShouldRestoreRecoveredDrafts()
	{
		var root = CreateWorkspaceFixture();
		try
		{
			var writer = new InMemoryWorkspaceService(enableBackgroundAutoSave: false);
			writer.OpenWorkspace(root);
			var tabRes = writer.OpenDocument(Path.Combine(root, "README.md"));
			var tab = tabRes.Tab!;
			writer.UpdateDocumentDraft(tab.DocumentId, "# Recovered draft");
			writer.FlushPendingAutoSaves();

			var reloaded = new InMemoryWorkspaceService(enableBackgroundAutoSave: false);
			var state = reloaded.OpenWorkspace(root);

			Assert.Single(state.OpenTabs);
			Assert.True(state.OpenTabs[0].IsDirty);
			Assert.Equal(tab.DocumentId, state.ActiveDocumentId);
			Assert.Equal("# Recovered draft", reloaded.GetDraftContent(tab.DocumentId));
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void RefreshWorkspaceFromDisk_ShouldUpdateTreeAndCleanDrafts()
	{
		var root = CreateWorkspaceFixture();
		try
		{
			var service = new InMemoryWorkspaceService(enableBackgroundAutoSave: false);
			service.OpenWorkspace(root);
			var tabRes = service.OpenDocument(Path.Combine(root, "README.md"));
			var tab = tabRes.Tab!;

			File.WriteAllText(Path.Combine(root, "README.md"), "# Changed");
			File.WriteAllText(Path.Combine(root, "new-note.md"), "new note");

			var refreshed = service.RefreshWorkspaceFromDisk();

			Assert.Contains(refreshed.FileTree[0].Children, node => !node.IsDirectory && node.Name == "new-note.md");
			Assert.Equal("# Changed", service.GetDraftContent(tab.DocumentId));
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void RefreshWorkspaceFromDisk_WhenDirtyAndDiskChanged_ShouldMarkConflictWithoutOverwritingDraft()
	{
		var root = CreateWorkspaceFixture();
		try
		{
			var service = new InMemoryWorkspaceService(enableBackgroundAutoSave: false);
			service.OpenWorkspace(root);
			var tabRes = service.OpenDocument(Path.Combine(root, "README.md"));
			var tab = tabRes.Tab!;
			service.UpdateDocumentDraft(tab.DocumentId, "# Local draft");

			File.WriteAllText(Path.Combine(root, "README.md"), "# External change");

			var refreshed = service.RefreshWorkspaceFromDisk();
			var activeTab = refreshed.OpenTabs.First(item => item.DocumentId == tab.DocumentId);

			Assert.True(activeTab.IsDirty);
			Assert.True(activeTab.HasExternalConflict);
			Assert.Contains("外部文件变更", activeTab.ConflictMessage);
			Assert.Equal("# Local draft", service.GetDraftContent(tab.DocumentId));
			Assert.Contains(service.GetConflictEvents(), item => item.DocumentId == tab.DocumentId && item.Action == "detected_external_change");
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void ResolveConflictBySavingLocal_ShouldClearConflictAndPersistLocalDraft()
	{
		var root = CreateWorkspaceFixture();
		try
		{
			var service = new InMemoryWorkspaceService(enableBackgroundAutoSave: false);
			service.OpenWorkspace(root);
			var tabRes = service.OpenDocument(Path.Combine(root, "README.md"));
			var tab = tabRes.Tab!;
			service.UpdateDocumentDraft(tab.DocumentId, "# Local draft");
			File.WriteAllText(Path.Combine(root, "README.md"), "# External change");
			service.RefreshWorkspaceFromDisk();

			var result = service.ResolveConflictBySavingLocal(tab.DocumentId, "# Local draft");
			var updatedTab = service.GetState().OpenTabs.First(item => item.DocumentId == tab.DocumentId);

			Assert.True(result.Succeeded);
			Assert.False(updatedTab.HasExternalConflict);
			Assert.False(updatedTab.IsDirty);
			Assert.Equal("# Local draft", File.ReadAllText(Path.Combine(root, "README.md")));
			Assert.Contains(service.GetConflictEvents(), item => item.DocumentId == tab.DocumentId && item.Action == "resolved_save_local");
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void ResolveConflictByReloadingFromDisk_ShouldDiscardLocalDraftAndClearConflict()
	{
		var root = CreateWorkspaceFixture();
		try
		{
			var service = new InMemoryWorkspaceService(enableBackgroundAutoSave: false);
			service.OpenWorkspace(root);
			var tabRes = service.OpenDocument(Path.Combine(root, "README.md"));
			var tab = tabRes.Tab!;
			service.UpdateDocumentDraft(tab.DocumentId, "# Local draft");
			File.WriteAllText(Path.Combine(root, "README.md"), "# External change");
			service.RefreshWorkspaceFromDisk();

			var result = service.ResolveConflictByReloadingFromDisk(tab.DocumentId);
			var updatedTab = service.GetState().OpenTabs.First(item => item.DocumentId == tab.DocumentId);

			Assert.True(result.Succeeded);
			Assert.False(updatedTab.HasExternalConflict);
			Assert.False(updatedTab.IsDirty);
			Assert.Equal("# External change", service.GetDraftContent(tab.DocumentId));
			Assert.Contains(service.GetConflictEvents(), item => item.DocumentId == tab.DocumentId && item.Action == "resolved_reload_external");
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void WorkspaceTabState_SupportsHasUnsavedRecoveryAndIsMissingOnDisk()
	{
		var tab = new WorkspaceTabState("id", "/path/file.md", false, DateTimeOffset.UtcNow)
		{
			HasUnsavedRecovery = true,
			IsMissingOnDisk = true
		};

		Assert.True(tab.HasUnsavedRecovery);
		Assert.True(tab.IsMissingOnDisk);
	}

	[Fact]
	public void NewInterfaceMethods_ShouldReturnExpectedResults()
	{
		var service = new InMemoryWorkspaceService();

		var createResult = service.CreateNode("/tmp", "test.md", false);
		Assert.Equal("outside_workspace", createResult.Code);

		var renameResult = service.RenameNode("/tmp/test.md", "renamed.md");
		Assert.Equal("outside_workspace", renameResult.Code);

		var removeResult = service.RemoveNode("/tmp/test.md");
		Assert.Equal("outside_workspace", removeResult.Code);

		var moveResult = service.MoveNode("/tmp/test.md", "/tmp/sub");
		Assert.Equal("outside_workspace", moveResult.Code);

		var closeRemoveResult = service.CloseAndRemove("/tmp/test.md");
		Assert.True(closeRemoveResult.Succeeded);

		var closeMoveResult = service.CloseAndMove("/tmp/test.md", "/tmp/sub");
		Assert.Equal("outside_workspace", closeMoveResult.Code);

		var session = service.GetLastSession();
		Assert.Null(session);

		service.FlushSession();
		service.InvalidateSession();

		var closed = service.GetRecentlyClosed();
		Assert.Empty(closed);

		service.RemoveFromRecentlyClosed("/tmp/test.md");
	}

	[Fact]
	public void CreateNode_ShouldCreateFileAndRefreshTree()
	{
		var root = CreateWorkspaceFixture();
		try
		{
			var service = new InMemoryWorkspaceService();
			service.OpenWorkspace(root);

			var result = service.CreateNode(root, "newfile.md", false);

			Assert.True(result.Succeeded, result.Message);
			Assert.Equal("created", result.Code);
			Assert.NotNull(result.AffectedPath);
			Assert.True(File.Exists(Path.Combine(root, "newfile.md")));
			var tree = service.GetState().FileTree;
			Assert.Contains(tree[0].Children, n => n.Name == "newfile.md");
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void CreateNode_ShouldCreateDirectoryAndRefreshTree()
	{
		var root = CreateWorkspaceFixture();
		try
		{
			var service = new InMemoryWorkspaceService();
			service.OpenWorkspace(root);

			var result = service.CreateNode(root, "subdir", true);

			Assert.True(result.Succeeded, result.Message);
			Assert.Equal("created_directory", result.Code);
			Assert.True(Directory.Exists(Path.Combine(root, "subdir")));
			var tree = service.GetState().FileTree;
			Assert.Contains(tree[0].Children, n => n.IsDirectory && n.Name == "subdir");
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void CreateNode_InvalidName_ShouldReturnInvalidName()
	{
		var root = CreateWorkspaceFixture();
		try
		{
			var service = new InMemoryWorkspaceService();
			service.OpenWorkspace(root);

			var result = service.CreateNode(root, ".hidden", false);
			Assert.False(result.Succeeded);
			Assert.Equal("invalid_name", result.Code);

			result = service.CreateNode(root, "", false);
			Assert.False(result.Succeeded);
			Assert.Equal("invalid_name", result.Code);

			result = service.CreateNode(root, "bad<char>", false);
			Assert.False(result.Succeeded);
			Assert.Equal("invalid_name", result.Code);
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void CreateNode_PathConflict_ShouldReturnPathConflict()
	{
		var root = CreateWorkspaceFixture();
		try
		{
			var service = new InMemoryWorkspaceService();
			service.OpenWorkspace(root);

			var result = service.CreateNode(root, "README.md", false);
			Assert.False(result.Succeeded);
			Assert.Equal("path_conflict", result.Code);
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void CreateNode_OutsideWorkspace_ShouldReturnOutsideWorkspace()
	{
		var service = new InMemoryWorkspaceService();
		var outside = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		try
		{
			var result = service.CreateNode(outside, "test.md", false);
			Assert.False(result.Succeeded);
			Assert.Equal("outside_workspace", result.Code);
		}
		finally
		{
			if (Directory.Exists(outside)) Directory.Delete(outside, true);
		}
	}

	[Fact]
	public void RenameNode_ShouldRenameFileAndUpdateTabPath()
	{
		var root = CreateWorkspaceFixture();
		try
		{
			var service = new InMemoryWorkspaceService();
			service.OpenWorkspace(root);
			var tabRes = service.OpenDocument(Path.Combine(root, "README.md"));
			var tab = tabRes.Tab!;

			var result = service.RenameNode(Path.Combine(root, "README.md"), "NEWREADME.md");

			Assert.True(result.Succeeded, result.Message);
			Assert.Equal("renamed", result.Code);
			Assert.True(File.Exists(Path.Combine(root, "NEWREADME.md")));
			Assert.False(File.Exists(Path.Combine(root, "README.md")));
			var state = service.GetState();
			var updatedTab = state.OpenTabs.FirstOrDefault(t => t.FilePath.Contains("NEWREADME"));
			Assert.NotNull(updatedTab);
			Assert.Contains("NEWREADME", updatedTab.FilePath);
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void RenameNode_PathConflict_ShouldReturnPathConflict()
	{
		var root = CreateWorkspaceFixture();
		try
		{
			var service = new InMemoryWorkspaceService();
			service.OpenWorkspace(root);

			var result = service.RenameNode(Path.Combine(root, "README.md"), "notes.md");
			Assert.False(result.Succeeded);
			Assert.Equal("path_conflict", result.Code);
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void RemoveNode_ShouldDeleteFileAndAddToRecentlyClosed()
	{
		var root = CreateWorkspaceFixture();
		try
		{
			var service = new InMemoryWorkspaceService();
			service.OpenWorkspace(root);

			var result = service.RemoveNode(Path.Combine(root, "notes.md"));

			Assert.True(result.Succeeded, result.Message);
			Assert.Equal("removed", result.Code);
			Assert.False(File.Exists(Path.Combine(root, "notes.md")));

			var closed = service.GetRecentlyClosed();
			Assert.Contains(closed, e => e.FileName == "notes.md");
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void RemoveNode_DirtyTab_ShouldReturnOpenTabUnsaved()
	{
		var root = CreateWorkspaceFixture();
		try
		{
			var service = new InMemoryWorkspaceService();
			service.OpenWorkspace(root);
			var tabRes = service.OpenDocument(Path.Combine(root, "notes.md"));
			var tab = tabRes.Tab!;
			service.MarkDirty(tab.DocumentId, true);

			var result = service.RemoveNode(Path.Combine(root, "notes.md"));

			Assert.False(result.Succeeded);
			Assert.Equal("open_tab_unsaved", result.Code);
			Assert.True(File.Exists(Path.Combine(root, "notes.md")));
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void CloseAndRemove_ShouldCloseTabAndDeleteFile()
	{
		var root = CreateWorkspaceFixture();
		try
		{
			var service = new InMemoryWorkspaceService();
			service.OpenWorkspace(root);
			service.OpenDocument(Path.Combine(root, "README.md"));

			var result = service.CloseAndRemove(Path.Combine(root, "README.md"));

			Assert.True(result.Succeeded, result.Message);
			Assert.Equal("closed_and_removed", result.Code);
			Assert.False(File.Exists(Path.Combine(root, "README.md")));
			Assert.Empty(service.GetState().OpenTabs);
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void MoveNode_ShouldMoveFile()
	{
		var root = CreateWorkspaceFixture();
		try
		{
			var service = new InMemoryWorkspaceService();
			service.OpenWorkspace(root);

			var result = service.MoveNode(Path.Combine(root, "docs", "guide.md"), root);

			Assert.True(result.Succeeded, result.Message);
			Assert.Equal("moved", result.Code);
			Assert.True(File.Exists(Path.Combine(root, "guide.md")));
			Assert.False(File.Exists(Path.Combine(root, "docs", "guide.md")));
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void MoveNode_WithOpenTab_ShouldReturnOpenTabUnsaved()
	{
		var root = CreateWorkspaceFixture();
		try
		{
			var service = new InMemoryWorkspaceService();
			service.OpenWorkspace(root);
			var tabRes = service.OpenDocument(Path.Combine(root, "docs", "guide.md"));
			var tab = tabRes.Tab!;

			var result = service.MoveNode(Path.Combine(root, "docs", "guide.md"), root);
			Assert.False(result.Succeeded);
			Assert.Equal("open_tab_unsaved", result.Code);
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void CloseAndMove_ShouldMoveAndCloseTab()
	{
		var root = CreateWorkspaceFixture();
		try
		{
			var service = new InMemoryWorkspaceService();
			service.OpenWorkspace(root);
			service.OpenDocument(Path.Combine(root, "notes.md"));

			var subDir = Path.Combine(root, "subdir");
			Directory.CreateDirectory(subDir);

			var result = service.CloseAndMove(Path.Combine(root, "notes.md"), subDir);

			Assert.True(result.Succeeded, result.Message);
			Assert.Equal("closed_and_moved", result.Code);
			Assert.True(File.Exists(Path.Combine(subDir, "notes.md")));
			Assert.Empty(service.GetState().OpenTabs);
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void FlushSession_ThenGetLastSession_ShouldReturnTabIds()
	{
		var root = CreateWorkspaceFixture();
		try
		{
			var service = new InMemoryWorkspaceService();
			service.OpenWorkspace(root);
			var tabARes = service.OpenDocument(Path.Combine(root, "README.md"));
			var tabA = tabARes.Tab!;
			var tabBRes = service.OpenDocument(Path.Combine(root, "notes.md"));
			var tabB = tabBRes.Tab!;

			service.FlushSession();

			var session = service.GetLastSession();
			Assert.NotNull(session);
			Assert.Equal(2, session.OpenTabIds.Count);
			Assert.Contains(session.OpenTabIds, id => id == tabA.DocumentId);
			Assert.Contains(session.OpenTabIds, id => id == tabB.DocumentId);
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void RecentlyClosed_LRULimit()
	{
		var root = CreateWorkspaceFixture();
		try
		{
			var service = new InMemoryWorkspaceService();
			service.OpenWorkspace(root);

			for (int i = 0; i < 21; i++)
			{
				var filePath = Path.Combine(root, $"file{i}.md");
				File.WriteAllText(filePath, $"content{i}");
				var removeResult = service.RemoveNode(filePath);
				Assert.True(removeResult.Succeeded, $"RemoveNode failed for file{i}: {removeResult.Message}");
			}

			var closed = service.GetRecentlyClosed();
			Assert.Equal(20, closed.Count);
			Assert.Contains("file20.md", closed[0].FileName);
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void CreateNode_ForbiddenPathInsideDotMuse()
	{
		var root = CreateWorkspaceFixture();
		try
		{
			var service = new InMemoryWorkspaceService();
			service.OpenWorkspace(root);

			var result = service.CreateNode(Path.Combine(root, ".muse", "settings"), "test.md", false);
			Assert.False(result.Succeeded);
			Assert.Equal("forbidden_path", result.Code);
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void OpenDocument_BinaryFile_ShouldNotOpen()
	{
		var root = CreateWorkspaceFixture();
		try
		{
			var service = new InMemoryWorkspaceService();
			service.OpenWorkspace(root);
			var binPath = Path.Combine(root, "image.bin");
			File.WriteAllBytes(binPath, new byte[] { 0x00, 0x01, 0x02, 0x03 });

			var res = service.OpenDocument(binPath);
			var state = service.GetState();

			Assert.False(res.Succeeded);
			Assert.Null(res.Tab);
			Assert.DoesNotContain(state.OpenTabs, t => t.FilePath == Path.GetFullPath(binPath));
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	private static string CreateWorkspaceFixture()
	{
		var root = Path.Combine(Path.GetTempPath(), "muse-workspace-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		Directory.CreateDirectory(Path.Combine(root, "docs"));
		File.WriteAllText(Path.Combine(root, "README.md"), "# Readme");
		File.WriteAllText(Path.Combine(root, "notes.md"), "notes");
		File.WriteAllText(Path.Combine(root, "docs", "guide.md"), "guide");
		return root;
	}
}