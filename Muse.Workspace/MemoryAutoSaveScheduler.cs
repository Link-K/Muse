namespace Muse.Workspace;

public sealed class MemoryAutoSaveScheduler : IAutoSaveScheduler
{
	private readonly HashSet<string> _pending = new(StringComparer.Ordinal);

	public int PendingCount => _pending.Count;

	public void Schedule(string documentId)
	{
		if (string.IsNullOrWhiteSpace(documentId))
		{
			return;
		}

		_pending.Add(documentId);
	}

	public IReadOnlyList<string> DrainScheduled()
	{
		if (_pending.Count == 0)
		{
			return [];
		}

		var drained = _pending.ToArray();
		_pending.Clear();
		return drained;
	}
}
