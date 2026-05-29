namespace Muse.Editor.Rendering;

public sealed class MarkdownEngineAdapter
{
	private readonly IMarkdownBlockParser _parser;
	private readonly IRenderingTelemetry? _telemetry;

	public MarkdownEngineAdapter(IMarkdownBlockParser? parser = null, IRenderingTelemetry? telemetry = null)
	{
		_parser = parser ?? new MarkdownBlockParser();
		_telemetry = telemetry;
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
}
