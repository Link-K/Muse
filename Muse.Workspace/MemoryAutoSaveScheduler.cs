namespace Muse.Workspace;

public sealed class MemoryAutoSaveScheduler : IAutoSaveScheduler
{
	private readonly Dictionary<string, DateTimeOffset> _pending = new(StringComparer.Ordinal);
	private readonly TimeSpan _debounceWindow;
	private readonly Func<DateTimeOffset> _nowProvider;

	public MemoryAutoSaveScheduler(TimeSpan? debounceWindow = null, Func<DateTimeOffset>? nowProvider = null)
	{
		_debounceWindow = debounceWindow ?? TimeSpan.Zero;
		_nowProvider = nowProvider ?? (() => DateTimeOffset.UtcNow);
	}

	public int PendingCount => _pending.Count;

	public void Schedule(string documentId)
	{
		if (string.IsNullOrWhiteSpace(documentId))
		{
			return;
		}

		_pending[documentId] = _nowProvider().Add(_debounceWindow);
	}

	public IReadOnlyList<string> DrainScheduled()
	{
		return DrainReady(int.MaxValue);
	}

	public IReadOnlyList<string> DrainReady(int maxBatchSize)
	{
		if (_pending.Count == 0 || maxBatchSize <= 0)
		{
			return [];
		}

		var now = _nowProvider();
		var ready = _pending
			.Where(item => item.Value <= now)
			.OrderBy(item => item.Value)
			.ThenBy(item => item.Key, StringComparer.Ordinal)
			.Take(maxBatchSize)
			.ToArray();

		if (ready.Length == 0)
		{
			return [];
		}

		foreach (var item in ready)
		{
			_pending.Remove(item.Key);
		}

		return ready.Select(static item => item.Key).ToArray();
	}
}
