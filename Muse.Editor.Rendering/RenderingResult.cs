namespace Muse.Editor.Rendering;

public sealed record RenderingResult(
	bool Succeeded,
	IReadOnlyList<RenderedBlock> Blocks,
	IReadOnlyList<RenderDiagnostic> Diagnostics)
{
	public static RenderingResult Success(IReadOnlyList<RenderedBlock> blocks)
	{
		return new RenderingResult(true, blocks, Array.Empty<RenderDiagnostic>());
	}

	public static RenderingResult Failure(RenderDiagnostic diagnostic)
	{
		return new RenderingResult(false, Array.Empty<RenderedBlock>(), new[] { diagnostic });
	}
}
