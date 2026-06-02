using Markdig;
using System.Threading.Tasks;

namespace Muse.Editor.Rendering;

public sealed class MarkdownEngineAdapter : IMarkdownEngineAdapter
{
	private readonly IMarkdownBlockParser _parser;
	private readonly IRenderingTelemetry? _telemetry;
	private readonly MarkdownPipeline _pipeline;

	public MarkdownEngineAdapter(IMarkdownBlockParser? parser = null, IRenderingTelemetry? telemetry = null)
	{
		_parser = parser ?? new MarkdownBlockParser();
		_telemetry = telemetry;
		_pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
	}

	public RenderingResult Render(RenderRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);

		try
		{
			var blocks = _parser.Parse(request);
			return RenderingResult.Success(blocks);
		}
		catch (Exception ex)
		{
			var diagnostic = new RenderDiagnostic("rendering_failed", ex.Message);
			_telemetry?.RecordFailure(diagnostic);
			return RenderingResult.Failure(diagnostic);
		}
		}
    
	public Task<string> RenderToHtmlAsync(string markdown)
	{
		var md = markdown ?? string.Empty;
		var html = Markdig.Markdown.ToHtml(md, _pipeline);
		return Task.FromResult(html);
	}
}

