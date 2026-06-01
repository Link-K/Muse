using System;
using Muse.Workspace;
using Xunit;

namespace Muse.Workspace.Tests
{
	public class TabIndicatorsTests
	{
		[Fact]
		public void WorkspaceTabState_HasFlagsReflectChanges()
		{
			var now = DateTimeOffset.UtcNow;
			var tab = new WorkspaceTabState("doc1", "file.md", true, now)
			{
				HasExternalConflict = true,
				ConflictMessage = "Conflict occurred"
			};

			Assert.True(tab.IsDirty);
			Assert.True(tab.HasExternalConflict);
			Assert.Equal("Conflict occurred", tab.ConflictMessage);
			Assert.Equal("doc1", tab.DocumentId);
			Assert.Equal(now, tab.LastTouchedAt);
		}
	}
}
