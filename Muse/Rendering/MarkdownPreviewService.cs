using System.Text;
using Muse.Editor.Rendering;
using Muse.ViewModels;

namespace Muse.Rendering;

public sealed class MarkdownPreviewService : IMarkdownPreviewService
{
	private readonly MarkdownEngineAdapter _engineAdapter;

	public MarkdownPreviewService(MarkdownEngineAdapter? engineAdapter = null)
	{
		_engineAdapter = engineAdapter ?? new MarkdownEngineAdapter();
	}

	public PreviewViewState Build(string markdown, EditorMode mode, string? theme = null)
	{
		var request = new RenderRequest(markdown ?? string.Empty, MapMode(mode), theme);
		var result = _engineAdapter.Render(request);

		if (!result.Succeeded)
		{
			var message = result.Diagnostics.Count > 0
				? result.Diagnostics[0].Message
				: "渲染失败。";
			return new PreviewViewState("预览生成失败。", true, message);
		}

		if (result.Blocks.Count == 0)
		{
			return new PreviewViewState("预览区（占位）：当前无内容", false, null);
		}

		var builder = new StringBuilder();
		for (var index = 0; index < result.Blocks.Count; index++)
		{
			if (index > 0)
			{
				builder.Append('\n');
			}

			builder.Append(result.Blocks[index].Source);
		}

		return new PreviewViewState(builder.ToString(), false, null);
	}

	private static RenderingMode MapMode(EditorMode mode)
	{
		return mode switch
		{
			EditorMode.Edit => RenderingMode.Edit,
			EditorMode.Split => RenderingMode.Split,
			_ => RenderingMode.Read
		};
	}
}
