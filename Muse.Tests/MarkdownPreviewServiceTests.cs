using Muse.Editor.Rendering;
using Muse.Rendering;
using Muse.ViewModels;
using Xunit;

namespace Muse.Tests;

public sealed class MarkdownPreviewServiceTests
{
	[Theory]
	[InlineData(EditorMode.Edit, RenderingMode.Edit)]
	[InlineData(EditorMode.Split, RenderingMode.Split)]
	[InlineData(EditorMode.Read, RenderingMode.Read)]
	public void Build_ShouldMapEditorModeToRenderingMode(EditorMode editorMode, RenderingMode expectedRenderingMode)
	{
		var gateway = new FakeGateway();
		var service = new MarkdownPreviewService(gateway);

		var viewState = service.Build("# title", editorMode, "light");

		Assert.Equal(expectedRenderingMode, gateway.LastRequest!.Mode);
		Assert.False(viewState.HasDiagnostics);
	}

	[Fact]
	public void Build_ShouldReturnPlaceholder_WhenNoBlocksReturned()
	{
		var gateway = new FakeGateway
		{
			ResultFactory = static _ => RenderingResult.Success(Array.Empty<RenderedBlock>())
		};
		var service = new MarkdownPreviewService(gateway);

		var viewState = service.Build(string.Empty, EditorMode.Edit);

		Assert.Equal("预览区（占位）：当前无内容", viewState.PreviewText);
		Assert.False(viewState.HasDiagnostics);
		Assert.Null(viewState.DiagnosticMessage);
	}

	[Fact]
	public void Build_ShouldReturnDiagnostic_WhenRenderingFailed()
	{
		var gateway = new FakeGateway
		{
			ResultFactory = static _ => RenderingResult.Failure(new RenderDiagnostic("rendering_failed", "boom"))
		};
		var service = new MarkdownPreviewService(gateway);

		var viewState = service.Build("# broken", EditorMode.Read);

		Assert.True(viewState.HasDiagnostics);
		Assert.Equal("预览生成失败。", viewState.PreviewText);
		Assert.Equal("boom", viewState.DiagnosticMessage);
	}

	private sealed class FakeGateway : IMarkdownRenderingGateway
	{
		public RenderRequest? LastRequest { get; private set; }

		public Func<RenderRequest, RenderingResult> ResultFactory { get; init; } = static request =>
			RenderingResult.Success(
			[
				new RenderedBlock(RenderedBlockKind.Paragraph, request.Markdown, request.Markdown, 1)
			]);

		public RenderingResult Render(RenderRequest request)
		{
			LastRequest = request;
			return ResultFactory(request);
		}
	}
}
