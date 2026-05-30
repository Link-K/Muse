namespace Muse.Workspace;

public interface IAutoSaveScheduler
{
	void Schedule(string documentId);

	IReadOnlyList<string> DrainScheduled();

	int PendingCount { get; }
}
