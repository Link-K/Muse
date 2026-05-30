using Muse.Editor.Rendering;

namespace Muse.Rendering;

public sealed class MarkdownRenderingGateway : IMarkdownRenderingGateway
{
	private readonly MarkdownEngineAdapter _adapter;

	public MarkdownRenderingGateway(MarkdownEngineAdapter? adapter = null)
	{
		_adapter = adapter ?? new MarkdownEngineAdapter();
	}

	public RenderingResult Render(RenderRequest request)
	{
		return _adapter.Render(request);
	}
}
