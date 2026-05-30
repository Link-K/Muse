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
			service.OpenDocument(Path.Combine(root, "README.md"));

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
	public void OpenAndActivateDocument_ShouldSwitchActiveTab()
	{
		var root = CreateWorkspaceFixture();
		try
		{
			var service = new InMemoryWorkspaceService();
			service.OpenWorkspace(root);
			var tabA = service.OpenDocument(Path.Combine(root, "README.md"));
			var tabB = service.OpenDocument(Path.Combine(root, "notes.md"));

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
			var tab = service.OpenDocument(Path.Combine(root, "README.md"));

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
			var tab = service.OpenDocument(Path.Combine(root, "README.md"));
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
			var tab = service.OpenDocument(Path.Combine(root, "missing-dir", "new.md"));
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
			var tab = writer.OpenDocument(Path.Combine(root, "README.md"));
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
			var tab = service.OpenDocument(Path.Combine(root, "README.md"));

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
