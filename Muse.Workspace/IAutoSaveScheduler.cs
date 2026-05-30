namespace Muse.Workspace;

public interface IAutoSaveScheduler
{
	void Schedule(string documentId);

	IReadOnlyList<string> DrainScheduled();

	IReadOnlyList<string> DrainReady(int maxBatchSize);

	int PendingCount { get; }
}
