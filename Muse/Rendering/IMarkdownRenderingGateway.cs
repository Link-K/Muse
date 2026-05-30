using Muse.Editor.Rendering;

namespace Muse.Rendering;

public interface IMarkdownRenderingGateway
{
	RenderingResult Render(RenderRequest request);
}
