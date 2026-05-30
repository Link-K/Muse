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

			var saved = service.SaveDocument(tab.DocumentId);
			var state = service.GetState();

			Assert.True(saved.Succeeded);
			Assert.Equal("saved", saved.Code);
			Assert.NotNull(saved.Tab);
			Assert.False(saved.Tab!.IsDirty);
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

			var result = service.SaveDocument(Path.Combine(root, "missing.md"));

			Assert.False(result.Succeeded);
			Assert.Equal("document_not_found", result.Code);
			Assert.Null(result.Tab);
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
