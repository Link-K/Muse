namespace Muse.Editor.Rendering;

internal sealed class MarkdownBlockParser : IMarkdownBlockParser
{
	public IReadOnlyList<RenderedBlock> Parse(RenderRequest request)
	{
		var lines = request.Markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
		var blocks = new List<RenderedBlock>(lines.Length);
		var inCodeFence = false;

		for (var index = 0; index < lines.Length; index++)
		{
			var line = lines[index];
			var trimmed = line.Trim();
			var lineNumber = index + 1;

			if (trimmed.Length == 0)
			{
				blocks.Add(new RenderedBlock(RenderedBlockKind.Empty, line, string.Empty, lineNumber));
				continue;
			}

			if (trimmed.StartsWith("```", StringComparison.Ordinal))
			{
				inCodeFence = !inCodeFence;
				blocks.Add(new RenderedBlock(RenderedBlockKind.CodeFence, line, trimmed, lineNumber));
				continue;
			}

			if (inCodeFence)
			{
				blocks.Add(new RenderedBlock(RenderedBlockKind.CodeFence, line, trimmed, lineNumber));
				continue;
			}

			if (trimmed.StartsWith("#", StringComparison.Ordinal))
			{
				blocks.Add(new RenderedBlock(RenderedBlockKind.Heading, line, trimmed.TrimStart('#', ' '), lineNumber));
				continue;
			}

			if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
			{
				blocks.Add(new RenderedBlock(RenderedBlockKind.ListItem, line, trimmed[2..], lineNumber));
				continue;
			}

			if (trimmed.Contains('|', StringComparison.Ordinal))
			{
				blocks.Add(new RenderedBlock(RenderedBlockKind.TableRow, line, trimmed, lineNumber));
				continue;
			}

			blocks.Add(new RenderedBlock(RenderedBlockKind.Paragraph, line, trimmed, lineNumber));
		}

		return blocks;
	}
}
