using Muse.Editor.Rendering;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Muse.Services;
using Muse;

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

		// Initialize commands to real implementations that use IClipboardService when available.
		CopyCodeCommand = new ActionCommand(() =>
		{
			_ = CopyCodeAsync();
		});

		CopyAnchorCommand = new ActionCommand(() =>
		{
			_ = CopyAnchorAsync();
		});
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

	private IClipboardService ResolveClipboard()
	{
		try
		{
			// Prefer DI when available
			return App.Resolve<IClipboardService>();
		}
		catch
		{
			// Fallback to runtime implementation
			return new AvaloniaClipboardService();
		}
	}

	private async Task CopyCodeAsync()
	{
		try
		{
			var svc = ResolveClipboard();
			await svc.SetTextAsync(Content ?? string.Empty).ConfigureAwait(false);
		}
		catch
		{
			// best-effort: swallow exceptions to avoid breaking UI
		}
	}

	private static string Slugify(string s)
	{
		if (string.IsNullOrWhiteSpace(s)) return string.Empty;
		var lowered = s.ToLowerInvariant();
		var arr = lowered.Select(c => (char.IsLetterOrDigit(c) ? c : '-')).ToArray();
		var joined = new string(arr);
		// collapse multiple dashes
		while (joined.Contains("--")) joined = joined.Replace("--", "-");
		return joined.Trim('-');
	}

	private async Task CopyAnchorAsync()
	{
		try
		{
			if (!IsHeading) return;
			var anchor = Slugify(Content ?? string.Empty);
			if (string.IsNullOrWhiteSpace(anchor)) return;
			var svc = ResolveClipboard();
			await svc.SetTextAsync(anchor).ConfigureAwait(false);
		}
		catch
		{
			// swallow
		}
	}

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
