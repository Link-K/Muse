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

			// move tabB to index 0 and persist
			var moved = service.MoveTab(tabB.DocumentId, 0);
			Assert.True(moved);

			// create a fresh service and reopen workspace to verify restoration
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

			// move tabB to index 0
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
			var tab = new Muse.Workspace.WorkspaceTabState("id", "/path/file.md", false, DateTimeOffset.UtcNow)
			{
				HasUnsavedRecovery = true,
				IsMissingOnDisk = true
			};

			Assert.True(tab.HasUnsavedRecovery);
			Assert.True(tab.IsMissingOnDisk);
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

	[Fact]
	public void OpenDocument_BinaryFile_ShouldNotOpen()
	{
		var root = CreateWorkspaceFixture();
		try
		{
			var service = new InMemoryWorkspaceService();
			service.OpenWorkspace(root);
			var binPath = Path.Combine(root, "image.bin");
			// write a small binary with a NUL byte
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
}
