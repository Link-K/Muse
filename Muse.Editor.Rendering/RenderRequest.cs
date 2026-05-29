namespace Muse.Editor.Rendering;

public sealed record RenderRequest(
	string Markdown,
	RenderingMode Mode,
	string? Theme = null);
