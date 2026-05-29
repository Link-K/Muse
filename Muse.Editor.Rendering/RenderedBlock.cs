namespace Muse.Editor.Rendering;

public sealed record RenderedBlock(
	RenderedBlockKind Kind,
	string Source,
	string Content,
	int LineNumber);
