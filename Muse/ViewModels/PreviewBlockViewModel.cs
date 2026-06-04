using Muse.Editor.Rendering;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Muse.Services;
using Muse;
using System.ComponentModel;

namespace Muse.ViewModels;

public sealed class PreviewBlockViewModel : INotifyPropertyChanged
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

		// detect image markdown like ![alt](url) or ![](url) and resolve local file path when possible
		TryParseImageFromContent(Content);

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

	private bool _isImage;
	public bool IsImage
	{
		get => _isImage;
		private set
		{
			if (_isImage != value)
			{
				try { Console.WriteLine($"[DEBUG] Setting IsImage = {value}"); } catch { }
				_isImage = value;
				OnPropertyChanged(nameof(IsImage));
			}
		}
	}

	private string? _imageFilePath;
	public string? ImageFilePath
	{
		get => _imageFilePath;
		private set
		{
			if (_imageFilePath != value)
			{
				try { Console.WriteLine($"[DEBUG] Setting ImageFilePath = {value}"); } catch { }
				_imageFilePath = value;
				OnPropertyChanged(nameof(ImageFilePath));
			}
		}
	}

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

	private void TryParseImageFromContent(string content)
	{
		try
		{
			try { Console.WriteLine($"[DEBUG] TryParseImageFromContent content='{content}'"); } catch { }
			if (string.IsNullOrWhiteSpace(content)) return;
			// simple parse: look for ![alt](url)
			var start = content.IndexOf("![", StringComparison.Ordinal);
			if (start < 0) return;
			var openParen = content.IndexOf('(', start);
			var closeParen = content.IndexOf(')', openParen + 1);
			if (openParen <= 0 || closeParen <= openParen) return;
			var url = content.Substring(openParen + 1, closeParen - openParen - 1).Trim();
			try { Console.WriteLine($"[DEBUG] Parsed image url: '{url}'"); } catch { }
			if (string.IsNullOrWhiteSpace(url)) return;
			// ignore absolute URLs (http/https/data)
			if (url.StartsWith("http:", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https:", StringComparison.OrdinalIgnoreCase) || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return;
			// normalize leading slash
			if (url.StartsWith("/")) url = url.TrimStart('/');
			// attempt to resolve relative to the active document directory first (assets next to md file)
			try
			{
				var ws = App.Resolve<Muse.Workspace.IWorkspaceService>();
				var state = ws?.GetState();
				var activeId = state?.ActiveDocumentId;
				var activeTab = state?.OpenTabs?.FirstOrDefault(t => string.Equals(t.DocumentId, activeId, StringComparison.Ordinal));
				var candidates = new System.Collections.Generic.List<string>();
				if (activeTab is not null && !string.IsNullOrWhiteSpace(activeTab.FilePath))
				{
					var docDir = Path.GetDirectoryName(activeTab.FilePath) ?? Environment.CurrentDirectory;
					candidates.Add(Path.Combine(docDir, url.Replace('/', Path.DirectorySeparatorChar)));
					candidates.Add(Path.Combine(docDir, "assets", Path.GetFileName(url)));
				}
				// also check all open tabs' dirs
				var allOpenTabs = state?.OpenTabs;
				if (allOpenTabs is not null)
				{
					foreach (var t in allOpenTabs)
					{
						if (t is null || string.IsNullOrWhiteSpace(t.FilePath)) continue;
						var d = Path.GetDirectoryName(t.FilePath) ?? Environment.CurrentDirectory;
						candidates.Add(Path.Combine(d, url.Replace('/', Path.DirectorySeparatorChar)));
						candidates.Add(Path.Combine(d, "assets", Path.GetFileName(url)));
					}
				}
				// repo-level candidates
				var repoRootCandidate = FindRepoRoot(AppContext.BaseDirectory ?? Environment.CurrentDirectory);
				if (!string.IsNullOrWhiteSpace(repoRootCandidate))
				{
					candidates.Add(Path.Combine(repoRootCandidate, "files", url.Replace('/', Path.DirectorySeparatorChar)));
					candidates.Add(Path.Combine(repoRootCandidate, "files", "assets", Path.GetFileName(url)));
				}
				// also check current working dir
				candidates.Add(Path.GetFullPath(url.Replace('/', Path.DirectorySeparatorChar)));
				candidates.Add(Path.Combine(Environment.CurrentDirectory, url.Replace('/', Path.DirectorySeparatorChar)));
				// dedupe candidates
				candidates = candidates.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
				foreach (var candidate in candidates)
				{
					try { Console.WriteLine($"[DEBUG] Checking candidate: {candidate}"); } catch { }
					if (File.Exists(candidate))
					{
						IsImage = true;
						ImageFilePath = candidate;
						try { Console.WriteLine($"[DEBUG] Image resolved to: {candidate}"); } catch { }
						return;
					}
				}
				try { Console.WriteLine($"[DEBUG] Active tab didn't resolve image, will try open tabs"); } catch { }
				// If not found in active tab, try all open tabs' assets (helpful in split view where preview context may differ)
				try
				{
					var openTabs = state?.OpenTabs;
					if (openTabs is not null)
					{
						foreach (var t in openTabs)
						{
							if (t is null || string.IsNullOrWhiteSpace(t.FilePath)) continue;
							var docDir2 = Path.GetDirectoryName(t.FilePath) ?? Environment.CurrentDirectory;
							var candidate2 = Path.Combine(docDir2, url.Replace('/', Path.DirectorySeparatorChar));
							if (File.Exists(candidate2))
							{
								IsImage = true;
								ImageFilePath = candidate2;
								return;
							}
							var assetCandidate2 = Path.Combine(docDir2, "assets", Path.GetFileName(url));
							if (File.Exists(assetCandidate2))
							{
								IsImage = true;
								ImageFilePath = assetCandidate2;
								return;
							}
						}
					}
				}
				catch { }
			}
			catch { }

			// If not resolved by workspace/OpenTabs, try Source-based and CWD fallbacks
			try
			{
				// if Source appears to be a file path, try its directory
				if (!string.IsNullOrWhiteSpace(Source))
				{
					try
					{
						var srcPath = Source.Trim();
						if (File.Exists(srcPath))
						{
							var srcDir = Path.GetDirectoryName(srcPath) ?? Environment.CurrentDirectory;
							var candidateS = Path.Combine(srcDir, url.Replace('/', Path.DirectorySeparatorChar));
							var assetCandidateS = Path.Combine(srcDir, "assets", Path.GetFileName(url));
							try { Console.WriteLine($"[DEBUG] Fallback Source check candidate: {candidateS}"); } catch { }
							if (File.Exists(candidateS)) { IsImage = true; ImageFilePath = candidateS; try { Console.WriteLine($"[DEBUG] Image resolved via Source to: {candidateS}"); } catch { } return; }
							try { Console.WriteLine($"[DEBUG] Fallback Source check assetCandidate: {assetCandidateS}"); } catch { }
							if (File.Exists(assetCandidateS)) { IsImage = true; ImageFilePath = assetCandidateS; try { Console.WriteLine($"[DEBUG] Image resolved via Source asset: {assetCandidateS}"); } catch { } return; }
						}
					}
					catch { }
				}
				// cwd
				try
				{
					var cwdCandidate = Path.Combine(Environment.CurrentDirectory, url.Replace('/', Path.DirectorySeparatorChar));
					var cwdAsset = Path.Combine(Environment.CurrentDirectory, "assets", Path.GetFileName(url));
					try { Console.WriteLine($"[DEBUG] Fallback CWD check candidate: {cwdCandidate}"); } catch { }
					if (File.Exists(cwdCandidate)) { IsImage = true; ImageFilePath = cwdCandidate; try { Console.WriteLine($"[DEBUG] Image resolved via CWD to: {cwdCandidate}"); } catch { } return; }
					try { Console.WriteLine($"[DEBUG] Fallback CWD check asset: {cwdAsset}"); } catch { }
					if (File.Exists(cwdAsset)) { IsImage = true; ImageFilePath = cwdAsset; try { Console.WriteLine($"[DEBUG] Image resolved via CWD asset: {cwdAsset}"); } catch { } return; }
				}
				catch { }
			}
			catch { }

			// fallback: attempt to resolve to repository files/<url>
			var repoRoot = FindRepoRoot(AppContext.BaseDirectory ?? Environment.CurrentDirectory);
			string candidatePath;
			if (!string.IsNullOrWhiteSpace(repoRoot))
			{
				candidatePath = Path.Combine(repoRoot, "files", url.Replace('/', Path.DirectorySeparatorChar));
			}
			else
			{
				candidatePath = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, url.Replace('/', Path.DirectorySeparatorChar)));
			}
			if (File.Exists(candidatePath))
			{
				IsImage = true;
				ImageFilePath = candidatePath;
			}

			// If still not found and url starts with assets/, try limited filesystem search for the filename
			if (!IsImage && url.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
			{
				var fileName = Path.GetFileName(url);
				try { Console.WriteLine($"[DEBUG] Beginning limited search for {fileName}"); } catch { }
				// 1) Walk up from AppContext.BaseDirectory and check parent/assets and parent/files/assets
				try
				{
					var dir = new DirectoryInfo(AppContext.BaseDirectory ?? Environment.CurrentDirectory);
					int depth = 0;
					while (dir != null && depth < 6)
					{
						var p1 = Path.Combine(dir.FullName, "assets", fileName);
						var p2 = Path.Combine(dir.FullName, "files", "assets", fileName);
						try { Console.WriteLine($"[DEBUG] LimitedSearch check: {p1}"); } catch { }
						if (File.Exists(p1)) { IsImage = true; ImageFilePath = p1; try { Console.WriteLine($"[DEBUG] LimitedSearch found: {p1}"); } catch { } break; }
						try { Console.WriteLine($"[DEBUG] LimitedSearch check: {p2}"); } catch { }
						if (File.Exists(p2)) { IsImage = true; ImageFilePath = p2; try { Console.WriteLine($"[DEBUG] LimitedSearch found: {p2}"); } catch { } break; }
						dir = dir.Parent;
						depth++;
					}
				}
				catch { }
				// 2) check user's Downloads common locations
				if (!IsImage)
				{
					try
					{
						var userDl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
						var candidates = new[] {
							Path.Combine(userDl, "assets", fileName),
							Path.Combine(userDl, "test", "assets", fileName),
							Path.Combine(userDl, fileName)
						};
						foreach (var c in candidates)
						{
							try { Console.WriteLine($"[DEBUG] Downloads check: {c}"); } catch { }
							if (File.Exists(c)) { IsImage = true; ImageFilePath = c; try { Console.WriteLine($"[DEBUG] Downloads found: {c}"); } catch { } break; }
						}
					}
					catch { }
				}
			}
		}
		catch { }
	}

	private static string? FindRepoRoot(string start)
	{
		try
		{
			var dir = new DirectoryInfo(start);
			while (dir != null)
			{
				if (dir.GetFiles("*.sln").Any() || Directory.Exists(Path.Combine(dir.FullName, ".git"))) return dir.FullName;
				dir = dir.Parent;
			}
		}
		catch { }
		return null;
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	private void OnPropertyChanged(string name)
	{
		try
		{
			// Ensure PropertyChanged is raised on UI thread for Avalonia bindings
			if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
			{
				_ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)));
				return;
			}
		}
		catch
		{
			// fall through to direct invoke if Dispatcher not available
		}

		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
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
