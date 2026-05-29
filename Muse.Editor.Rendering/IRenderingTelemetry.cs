namespace Muse.Editor.Rendering;

public interface IRenderingTelemetry
{
	void RecordFailure(RenderDiagnostic diagnostic);
}
