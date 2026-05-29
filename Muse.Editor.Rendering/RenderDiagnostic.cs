namespace Muse.Editor.Rendering;

public sealed record RenderDiagnostic(
	string Code,
	string Message,
	int? LineNumber = null);
