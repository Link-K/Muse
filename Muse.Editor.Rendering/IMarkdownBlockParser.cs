namespace Muse.Editor.Rendering;

public interface IMarkdownBlockParser
{
	IReadOnlyList<RenderedBlock> Parse(RenderRequest request);
}
