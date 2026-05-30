using Xunit;

namespace Muse.Workspace.Tests;

public sealed class MemoryAutoSaveSchedulerTests
{
	[Fact]
	public void Schedule_ShouldApplyDebounceWindow()
	{
		var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
		var scheduler = new MemoryAutoSaveScheduler(TimeSpan.FromSeconds(2), () => now);

		scheduler.Schedule("doc-1");
		Assert.Empty(scheduler.DrainScheduled());

		now = now.AddSeconds(1);
		scheduler.Schedule("doc-1");
		now = now.AddSeconds(1);
		Assert.Empty(scheduler.DrainScheduled());

		now = now.AddSeconds(1.1);
		var drained = scheduler.DrainScheduled();

		Assert.Single(drained);
		Assert.Equal("doc-1", drained[0]);
	}

	[Fact]
	public void DrainReady_ShouldRespectBatchSize()
	{
		var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
		var scheduler = new MemoryAutoSaveScheduler(TimeSpan.Zero, () => now);

		scheduler.Schedule("doc-a");
		scheduler.Schedule("doc-b");
		scheduler.Schedule("doc-c");

		var batch1 = scheduler.DrainReady(2);
		var batch2 = scheduler.DrainReady(2);

		Assert.Equal(2, batch1.Count);
		Assert.Single(batch2);
		Assert.Equal(0, scheduler.PendingCount);
	}
}
