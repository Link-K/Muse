using System;
using System.Text;
using Muse.Editor.Rendering;
using Muse.ViewModels;

namespace Muse.Rendering;

public sealed class MarkdownPreviewService : IMarkdownPreviewService
{
	private readonly IMarkdownRenderingGateway _renderingGateway;

	public MarkdownPreviewService(IMarkdownRenderingGateway? renderingGateway = null)
	{
		_renderingGateway = renderingGateway ?? new MarkdownRenderingGateway();
	}

	public PreviewViewState Build(string markdown, EditorMode mode, string? theme = null)
	{
		var request = new RenderRequest(markdown ?? string.Empty, MapMode(mode), theme);
		var result = _renderingGateway.Render(request);

		if (!result.Succeeded)
		{
			var message = result.Diagnostics.Count > 0
				? result.Diagnostics[0].Message
				: "渲染失败。";
			return new PreviewViewState("预览生成失败。", true, message, Array.Empty<RenderedBlock>());
		}

		if (result.Blocks.Count == 0)
		{
			return new PreviewViewState("预览区（占位）：当前无内容", false, null, Array.Empty<RenderedBlock>());
		}

		var builder = new StringBuilder();
		for (var index = 0; index < result.Blocks.Count; index++)
		{
			if (index > 0)
			{
				builder.Append('\n');
			}

			// 使用解析器提供的 Content（已去除 Markdown 标记的语义文本），
			// 以便在当前以 TextBlock 为主的预览区域中显示更接近渲染后的文本。
			builder.Append(result.Blocks[index].Content);
		}

		return new PreviewViewState(builder.ToString(), false, null, result.Blocks);
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
