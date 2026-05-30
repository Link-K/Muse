using Muse.ViewModels;

namespace Muse.Rendering;

public interface IMarkdownPreviewService
{
	PreviewViewState Build(string markdown, EditorMode mode, string? theme = null);
}
