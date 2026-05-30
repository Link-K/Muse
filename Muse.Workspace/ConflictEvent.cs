namespace Muse.Workspace;

public sealed record ConflictEvent(
	string DocumentId,
	string Action,
	string Message,
	DateTimeOffset OccurredAt);
