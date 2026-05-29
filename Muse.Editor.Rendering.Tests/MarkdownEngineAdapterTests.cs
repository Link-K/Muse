using Xunit;

namespace Muse.Editor.Rendering.Tests;

public sealed class MarkdownEngineAdapterTests
{
	[Fact]
	public void Render_ShouldClassifyBasicMarkdownBlocks()
	{
		const string markdown = "# Title\n- item\n```csharp\nConsole.WriteLine(1);\n```\n|A|B|\nparagraph";
		var adapter = new MarkdownEngineAdapter();

		var result = adapter.Render(new RenderRequest(markdown, RenderingMode.Edit, "light"));

		Assert.True(result.Succeeded);
		Assert.Contains(result.Blocks, block => block.Kind == RenderedBlockKind.Heading && block.Content == "Title");
		Assert.Contains(result.Blocks, block => block.Kind == RenderedBlockKind.ListItem && block.Content == "item");
		Assert.Contains(result.Blocks, block => block.Kind == RenderedBlockKind.CodeFence);
		Assert.Contains(result.Blocks, block => block.Kind == RenderedBlockKind.TableRow);
		Assert.Contains(result.Blocks, block => block.Kind == RenderedBlockKind.Paragraph && block.Content == "paragraph");
	}

	[Fact]
	public void Render_ShouldHandleEmptyAndLongParagraphInput()
	{
		var longParagraph = new string('a', 4096);
		var adapter = new MarkdownEngineAdapter();

		var result = adapter.Render(new RenderRequest($"\n{longParagraph}", RenderingMode.Split));

		Assert.True(result.Succeeded);
		Assert.Equal(RenderedBlockKind.Empty, result.Blocks[0].Kind);
		Assert.Equal(RenderedBlockKind.Paragraph, result.Blocks[1].Kind);
		Assert.Equal(longParagraph, result.Blocks[1].Content);
	}

	[Fact]
	public void Render_ShouldReturnDiagnosticAndRecordFailure_WhenParserThrows()
	{
		var telemetry = new FakeTelemetry();
		var adapter = new MarkdownEngineAdapter(new ThrowingParser(), telemetry);

		var result = adapter.Render(new RenderRequest("# broken", RenderingMode.Read));

		Assert.False(result.Succeeded);
		Assert.Single(result.Diagnostics);
		Assert.Equal("rendering_failed", result.Diagnostics[0].Code);
		Assert.Single(telemetry.Diagnostics);
	}

	private sealed class ThrowingParser : IMarkdownBlockParser
	{
		public IReadOnlyList<RenderedBlock> Parse(RenderRequest request)
		{
			throw new InvalidOperationException("parser exploded");
		}
	}

	private sealed class FakeTelemetry : IRenderingTelemetry
	{
		public List<RenderDiagnostic> Diagnostics { get; } = new();

		public void RecordFailure(RenderDiagnostic diagnostic)
		{
			Diagnostics.Add(diagnostic);
		}
	}
}
