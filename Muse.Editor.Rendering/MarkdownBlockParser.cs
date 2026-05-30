using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;

namespace Muse.Editor.Rendering;

internal sealed class MarkdownBlockParser : IMarkdownBlockParser
{
	private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
		.UseAdvancedExtensions()
		.Build();

	private static readonly Regex ListMarkerRegex = new("^\\s*(?:[-+*]|\\d+[.)])\\s+", RegexOptions.Compiled);

	public IReadOnlyList<RenderedBlock> Parse(RenderRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);

		var markdown = request.Markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
		var lines = markdown.Split('\n');
		var classifications = new LineClassification?[lines.Length];

		var document = Markdown.Parse(markdown, Pipeline);
		ClassifyBlocks(document, markdown, lines.Length, classifications);

		var blocks = new List<RenderedBlock>(lines.Length);
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

			var classification = classifications[index];
			if (classification is null)
			{
				if (trimmed.Contains('|', StringComparison.Ordinal))
				{
					blocks.Add(new RenderedBlock(RenderedBlockKind.TableRow, line, trimmed, lineNumber));
					continue;
				}

				blocks.Add(new RenderedBlock(RenderedBlockKind.Paragraph, line, trimmed, lineNumber));
				continue;
			}

			var kind = classification.Value.Kind;
			if (kind == RenderedBlockKind.Paragraph && trimmed.Contains('|', StringComparison.Ordinal))
			{
				kind = RenderedBlockKind.TableRow;
			}

			var content = GetContent(kind, trimmed);
			blocks.Add(new RenderedBlock(kind, line, content, lineNumber));
		}

		return blocks;
	}

	private static void ClassifyBlocks(ContainerBlock container, string markdown, int totalLines, LineClassification?[] classifications)
	{
		foreach (var block in container)
		{
			switch (block)
			{
				case HeadingBlock:
					ApplyRange(block, RenderedBlockKind.Heading, Priority.Structured, markdown, totalLines, classifications);
					break;
				case ListBlock list:
					foreach (var child in list)
					{
						if (child is ListItemBlock item)
						{
							ApplyRange(item, RenderedBlockKind.ListItem, Priority.Structured, markdown, totalLines, classifications);
						}
					}
					break;
				case FencedCodeBlock:
				case CodeBlock:
					ApplyRange(block, RenderedBlockKind.CodeFence, Priority.Structured, markdown, totalLines, classifications);
					break;
				case Table:
					ApplyRange(block, RenderedBlockKind.TableRow, Priority.Structured, markdown, totalLines, classifications);
					break;
				case ParagraphBlock:
					ApplyRange(block, RenderedBlockKind.Paragraph, Priority.Paragraph, markdown, totalLines, classifications);
					break;
				case ContainerBlock childContainer:
					ClassifyBlocks(childContainer, markdown, totalLines, classifications);
					break;
			}
		}
	}

	private static void ApplyRange(
		Block block,
		RenderedBlockKind kind,
		Priority priority,
		string markdown,
		int totalLines,
		LineClassification?[] classifications)
	{
		var (startLine, endLine) = GetLineRange(block, markdown, totalLines);
		for (var line = startLine; line <= endLine; line++)
		{
			var lineIndex = line - 1;
			var existing = classifications[lineIndex];
			if (existing is null || priority >= existing.Value.Priority)
			{
				classifications[lineIndex] = new LineClassification(kind, priority);
			}
		}
	}

	private static (int StartLine, int EndLine) GetLineRange(Block block, string markdown, int totalLines)
	{
		var start = block.Span.Start;
		var end = block.Span.End;

		if (start < 0 || end < start)
		{
			var fallbackLine = Math.Clamp(block.Line + 1, 1, totalLines);
			return (fallbackLine, fallbackLine);
		}

		var startLine = GetLineNumberFromIndex(markdown, start);
		var endLine = GetLineNumberFromIndex(markdown, end);
		return (Math.Clamp(startLine, 1, totalLines), Math.Clamp(endLine, 1, totalLines));
	}

	private static int GetLineNumberFromIndex(string markdown, int index)
	{
		var line = 1;
		var stop = Math.Min(index, markdown.Length - 1);
		for (var i = 0; i <= stop; i++)
		{
			if (markdown[i] == '\n')
			{
				line++;
			}
		}

		return line;
	}

	private static string GetContent(RenderedBlockKind kind, string trimmed)
	{
		return kind switch
		{
			RenderedBlockKind.Heading => trimmed.TrimStart('#', ' '),
			RenderedBlockKind.ListItem => ListMarkerRegex.Replace(trimmed, string.Empty),
			_ => trimmed
		};
	}

	private readonly record struct LineClassification(RenderedBlockKind Kind, Priority Priority);

	private enum Priority
	{
		Paragraph = 1,
		Structured = 2
	}
}
