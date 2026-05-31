using System.Collections.Generic;
using Muse.Editor.Rendering;

namespace Muse.Rendering;

public sealed record PreviewViewState(
	string PreviewText,
	bool HasDiagnostics,
	string? DiagnosticMessage,
	IReadOnlyList<RenderedBlock> Blocks);
