namespace Muse.Workspace;

public sealed record RecentlyClosedEntry(
    string FilePath,
    string FileName,
    DateTimeOffset ClosedAt,
    long? LastKnownSizeBytes);