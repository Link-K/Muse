using Muse.Editor.Rendering;
using System;
using System.Linq;
using System.Windows.Input;

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

		// Initialize commands as no-op placeholders; can be enhanced to interact with clipboard later.
		CopyCodeCommand = new ActionCommand(() => { });
		CopyAnchorCommand = new ActionCommand(() => { });
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

	// Extracted language for fenced code blocks (best-effort from Source)
	public string CodeFenceLanguage
	{
		get
		{
			if (!IsCodeFence || string.IsNullOrWhiteSpace(Source)) return string.Empty;
			var s = Source.Trim();
			if (!s.StartsWith("``")) return string.Empty;
			var parts = s.Trim('`').Split(' ', StringSplitOptions.RemoveEmptyEntries);
			return parts.Length > 0 ? parts[0] : string.Empty;
		}
	}

	public ICommand CopyCodeCommand { get; }

	public ICommand CopyAnchorCommand { get; }

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

internal sealed class ActionCommand : ICommand
{
	private readonly Action _action;
	public ActionCommand(Action action) => _action = action ?? throw new ArgumentNullException(nameof(action));
	public bool CanExecute(object? parameter) => true;
	public void Execute(object? parameter) => _action();
	public event EventHandler? CanExecuteChanged { add { } remove { } }
}
