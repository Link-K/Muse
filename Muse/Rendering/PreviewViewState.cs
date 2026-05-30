namespace Muse.Rendering;

public sealed record PreviewViewState(
	string PreviewText,
	bool HasDiagnostics,
	string? DiagnosticMessage);
