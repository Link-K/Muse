using Muse.Editor.Rendering;
using System;
using System.Linq;

namespace Muse.ViewModels;

public sealed class PreviewBlockViewModel
{
	public PreviewBlockViewModel(RenderedBlock block)
	{
		Kind = block.Kind;
		Content = block.Content;
		Source = block.Source;
		LineNumber = block.LineNumber;
		TableCells = BuildTableCells(Content);
		TableDisplayText = Content;
		TableRows = Array.Empty<PreviewTableRowViewModel>();
	}

	public RenderedBlockKind Kind { get; }

	public string Content { get; }

	public string Source { get; }

	public int LineNumber { get; }

	public bool IsHeading => Kind == RenderedBlockKind.Heading;
	public bool IsParagraph => Kind == RenderedBlockKind.Paragraph;
	public bool IsListItem => Kind == RenderedBlockKind.ListItem;
	public bool IsCodeFence => Kind == RenderedBlockKind.CodeFence;
	public bool IsTableRow => Kind == RenderedBlockKind.TableRow;
	public bool IsEmpty => Kind == RenderedBlockKind.Empty;

	public bool IsRenderable => !IsEmpty && !_suppressRendering;

	public string[] TableCells { get; }

	public bool HasTableCells => TableCells.Length > 0;

	public bool ShowTableCells => HasTableCells && !IsTableDivider;

	public bool IsTableDivider => IsTableRow && Source.Trim().All(static c => c == '|' || c == '-' || c == ':' || c == ' ');

	// Fallback plain text preview removed — UI will no longer show a fallback text block.

	public string TableDisplayText { get; private set; }

	public bool ShowAlignedTableText => IsTableRow && !IsTableDivider && !string.IsNullOrWhiteSpace(TableDisplayText);

	public PreviewTableRowViewModel[] TableRows { get; private set; }

	public bool ShowTableGrid => IsTableRow && TableRows.Length > 0;

	private bool _suppressRendering;

	internal void SetAlignedTableDisplayText(string value)
	{
		TableDisplayText = value;
	}

	internal void SetTableRows(PreviewTableRowViewModel[] rows)
	{
		TableRows = rows;
	}

	internal void SuppressRendering()
	{
		_suppressRendering = true;
	}

	private static string[] BuildTableCells(string content)
	{
		if (string.IsNullOrWhiteSpace(content))
		{
			return Array.Empty<string>();
		}

		var trimmed = content.Trim();
		if (!trimmed.Contains('|', StringComparison.Ordinal))
		{
			return new[] { trimmed };
		}

		return trimmed
			.Trim('|')
			.Split('|', StringSplitOptions.TrimEntries)
			.ToArray();
	}
}
