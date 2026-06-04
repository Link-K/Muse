using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Muse.ViewModels;
using Muse.Services;
using Muse.Assets;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using System.Linq;

namespace Muse.Views;

public partial class MainView : UserControl
{
	// Parameterless ctor kept for design-time compatibility; runtime should use DI constructor below.
	public MainView()
	{
		InitializeComponent();
		// 运行时优先从 App.Resolve<T>() 获取，设计时/测试时再回退到默认实现
		try
		{
			ClipboardService = App.Resolve<IClipboardService>();
		}
		catch
		{
			ClipboardService = new AvaloniaClipboardService();
		}
		try
		{
			FileDebugWriter = App.Resolve<IFileDebugWriter>();
		}
		catch
		{
			FileDebugWriter = new FileDebugWriter();
		}

		// hook editor-level handlers to ensure paste/drag events are captured even
		// if TextBox doesn't receive KeyDown. This complements XAML KeyDown handlers.
		try
		{
			HookEditorInputHandlers();
		}
		catch
		{
			// ignore
		}
	}

	private string? GetActiveDocumentPath()
	{
		try
		{
			if (DataContext is Muse.ViewModels.MainViewModel mvm && mvm.WorkspaceTabs is not null && mvm.WorkspaceTabs.Length > 0)
			{
				var activeTab = mvm.WorkspaceTabs.FirstOrDefault(t => t.IsActive);
				if (activeTab is not null && !string.IsNullOrWhiteSpace(activeTab.FilePath)) return activeTab.FilePath;
			}
			var ws = App.Resolve<Muse.Workspace.IWorkspaceService>();
			var state = ws?.GetState();
			var activeId = state?.ActiveDocumentId;
			var activeTab2 = state?.OpenTabs?.FirstOrDefault(t => string.Equals(t.DocumentId, activeId, StringComparison.Ordinal));
			if (activeTab2 is not null && !string.IsNullOrWhiteSpace(activeTab2.FilePath)) return activeTab2.FilePath;
		}
		catch { }
		return null;
	}

	private Avalonia.Point? _dragStartPoint;

	private void OnPasteImageClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		_ = HandlePasteAsync();
	}
	private string? _draggingDocumentId;
	private bool _isDragging;
	private const double DragThreshold = 6.0; // pixels

	// track current drop target to update visual indicator
	private string? _currentDropTargetDocumentId;

	private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (sender is not Control c) return;
		if (c.DataContext is not Muse.ViewModels.WorkspaceTabViewModel vm) return;
		_dragStartPoint = e.GetPosition(this);
		_draggingDocumentId = vm.DocumentId;
		_isDragging = false;
	}

	private void OnTabPointerMoved(object? sender, PointerEventArgs e)
	{
		if (_dragStartPoint is null || _draggingDocumentId is null) return;
		var pos = e.GetPosition(this);
		var dx = pos.X - _dragStartPoint.Value.X;
		var dy = pos.Y - _dragStartPoint.Value.Y;
		if (!_isDragging && Math.Sqrt(dx * dx + dy * dy) > DragThreshold)
		{
			_isDragging = true;
		}

		// if dragging, update drop target visual on the tab currently under pointer (sender bound per-tab)
		if (!_isDragging) return;
		try
		{
			if (sender is not Control c) return;
			if (c.DataContext is not Muse.ViewModels.WorkspaceTabViewModel targetVm) return;
			// compute pointer position relative to target control to decide before/after
			var local = e.GetPosition(c);
			var width = c.Bounds.Width;
			var isBefore = local.X < width / 2.0;
			// clear previous and set new flags
			if (!string.Equals(_currentDropTargetDocumentId, targetVm.DocumentId, StringComparison.Ordinal))
			{
				ClearCurrentDropTarget();
				_currentDropTargetDocumentId = targetVm.DocumentId;
			}
			targetVm.IsDropTarget = true;
			targetVm.IsDropBefore = isBefore;
			targetVm.IsDropAfter = !isBefore;
		}
		catch
		{
			// ignore visual update errors
		}
	}

	private void OnTabPointerReleased(object? sender, PointerReleasedEventArgs e)
	{
		try
		{
			if (!_isDragging || _draggingDocumentId is null) return;
			if (DataContext is not Muse.ViewModels.MainViewModel vm) return;
			if (sender is not Control targetControl) return;
			if (targetControl.DataContext is not Muse.ViewModels.WorkspaceTabViewModel targetVm) return;

			// find index of target in current workspace tabs
			var tabs = vm.WorkspaceTabs;
			int newIndex = Array.FindIndex(tabs, t => t.DocumentId == targetVm.DocumentId);
			if (newIndex < 0) return;

			// if dropping onto a different tab, move
			if (!string.Equals(_draggingDocumentId, targetVm.DocumentId, StringComparison.Ordinal))
			{
				vm.ReorderTabs(_draggingDocumentId, newIndex);
			}
		}
		finally
		{
			_dragStartPoint = null;
			_draggingDocumentId = null;
			_isDragging = false;
			ClearCurrentDropTarget();
		}
	}

	private void ClearCurrentDropTarget()
	{
		try
		{
			if (string.IsNullOrWhiteSpace(_currentDropTargetDocumentId)) return;
			if (DataContext is not Muse.ViewModels.MainViewModel vm) return;
			var tabs = vm.WorkspaceTabs;
			foreach (var t in tabs)
			{
				if (string.Equals(t.DocumentId, _currentDropTargetDocumentId, StringComparison.Ordinal))
				{
					t.IsDropTarget = false;
					t.IsDropBefore = false;
					t.IsDropAfter = false;
					break;
				}
			}
		}
		catch
		{
			// ignore
		}
		finally
		{
			_currentDropTargetDocumentId = null;
		}
	}

	private void OnFileTreeNodeDoubleTapped(object? sender, TappedEventArgs e)
	{
		if (sender is not Control c) return;
		if (c.DataContext is not FileTreeNodeViewModel node) return;
		if (node.IsDirectory) return;

		if (node.OpenCommand?.CanExecute(null) == true)
		{
			node.OpenCommand.Execute(null);
		}
	}

	private void OnFileTreeNodeTapped(object? sender, TappedEventArgs e)
	{
		if (sender is not Control c) return;
		if (c.DataContext is not FileTreeNodeViewModel node) return;
		// Only toggle expansion for directories on single tap
		if (!node.IsDirectory) return;
		if (node.ToggleExpandedCommand?.CanExecute(null) == true)
		{
			node.ToggleExpandedCommand.Execute(null);
		}
	}

	// 防抖：忽略短时间内重复的粘贴触发（可能来自低级钩子与 KeyDown 双重触发）
	private readonly object _pasteLock = new();
	private DateTimeOffset? _lastPasteAt;
	private (int Caret, int SelStart, int SelLen, int ImgLen) _lastPasteKey;
	private static readonly TimeSpan PasteDuplicateWindow = TimeSpan.FromMilliseconds(800);

	// DI constructor used in production when resolving via IServiceProvider
	public MainView(MainViewModel viewModel, IClipboardService clipboardService, IFileDebugWriter fileDebugWriter)
	{
		InitializeComponent();
		DataContext = viewModel;
		ClipboardService = clipboardService;
		FileDebugWriter = fileDebugWriter;

		// 订阅 ViewModel 的不支持格式对话框请求事件，视图负责以模态窗口展示
		if (viewModel is not null)
		{
			var top = TopLevel.GetTopLevel(this) as Window;
			// Prefer container-resolved IDialogService when available; fall back to inline dialog.
			Muse.Services.IDialogService? ds = App.ServiceProvider?.GetService(typeof(Muse.Services.IDialogService)) as Muse.Services.IDialogService;

			viewModel.ShowUnsupportedFileDialogRequested += async (title, message) =>
			{
				try
				{
					if (ds is not null)
					{
						await ds.ShowMessageAsync(title, message ?? string.Empty);
					}
					else
					{
						try { await FileDebugWriter?.WriteDebugFileAsync($"Unsupported file dialog requested: {title} - {message}"); } catch { }
						try { Console.WriteLine($"[DEBUG] Unsupported file dialog requested: {title}"); } catch { }
					}
				}
				catch
				{
					// ignore display errors
				}
			};
		}

		try
		{
			HookEditorInputHandlers();
		}
		catch
		{
			// ignore
		}
	}

	private void HookEditorInputHandlers()
	{
		// ensure handlers are attached after control is part of a TopLevel
		if (TopLevel.GetTopLevel(this) is null)
		{
			this.AttachedToVisualTree += (_, _) => AttachInputHandlersWhenReady();
			this.DetachedFromVisualTree += (_, _) => DetachInputHandlers();
			return;
		}
		AttachInputHandlersWhenReady();
	}

	private bool _inputHandlersAttached = false;

	private void AttachInputHandlersWhenReady()
	{
		if (_inputHandlersAttached) return;
		_inputHandlersAttached = true;
		var top = TopLevel.GetTopLevel(this);
		if (top is not null)
		{
			try
			{
				top.KeyDown += Top_KeyDown;
				top.AddHandler(DragDrop.DropEvent, Top_DropHandler);
				Console.WriteLine("[DEBUG] Attached top-level input handlers.");
			}
			catch
			{
				// ignore
			}
		}

		// attach editor-level drag/drop + a simple ContextMenu Paste fallback
		var tbNames = new[] { "EditorTextBox", "SplitSourceTextBox", "SplitSourceTextBoxV" };
		foreach (var name in tbNames)
		{
			try
			{
				var tb = this.FindControl<TextBox>(name);
				if (tb is null) continue;

				// Enable drop via attached property when available in current Avalonia runtime.
				try
				{
					var setAllowDrop = typeof(DragDrop).GetMethod("SetAllowDrop", new[] { typeof(AvaloniaObject), typeof(bool) });
					setAllowDrop?.Invoke(null, new object?[] { tb, true });
				}
				catch
				{
					// ignore
				}

				tb.AddHandler(DragDrop.DragOverEvent, Editor_DragOverHandler);
				tb.AddHandler(DragDrop.DropEvent, Editor_DropHandler);

				var menu = new ContextMenu();
				var pasteItem = new MenuItem { Header = "粘贴" };
				pasteItem.Click += async (_, __) => { try { await HandlePasteAsync().ConfigureAwait(true); } catch { } };
				if (menu.Items is System.Collections.IList list) list.Add(pasteItem);
				tb.ContextMenu = menu;
			}
			catch
			{
				// ignore per-control errors
			}
		}
	}

	private void DetachInputHandlers()
	{
		if (!_inputHandlersAttached) return;
		_inputHandlersAttached = false;
		var top = TopLevel.GetTopLevel(this);
		if (top is not null)
		{
			try
			{
				top.KeyDown -= Top_KeyDown;
				top.RemoveHandler(DragDrop.DropEvent, Top_DropHandler);
			}
			catch { }
		}

		var tbNames = new[] { "EditorTextBox", "SplitSourceTextBox", "SplitSourceTextBoxV" };
		foreach (var name in tbNames)
		{
			try
			{
				var tb = this.FindControl<TextBox>(name);
				if (tb is null) continue;
				tb.RemoveHandler(DragDrop.DragOverEvent, Editor_DragOverHandler);
				tb.RemoveHandler(DragDrop.DropEvent, Editor_DropHandler);
			}
			catch { }
		}
	}

	private void Top_KeyDown(object? sender, KeyEventArgs e)
	{
		try
		{
			// Ctrl+V: paste image (existing behavior)
			if (e.Key == Key.V && (e.KeyModifiers & KeyModifiers.Control) != 0)
			{
				_ = HandlePasteAsync();
				e.Handled = true;
			}
			// Ctrl+S: save active document
			else if (e.Key == Key.S && (e.KeyModifiers & KeyModifiers.Control) != 0)
			{
				try
				{
					if (DataContext is Muse.ViewModels.MainViewModel vm)
					{
						var cmd = vm.SaveActiveDocumentCommand;
						if (cmd?.CanExecute(null) == true) cmd.Execute(null);
					}
				}
				catch { }
				e.Handled = true;
			}
		}
		catch { }
	}

	private void Top_DropHandler(object? sender, DragEventArgs e)
	{
		try
		{
			_ = HandleDropAsync(e);
			e.Handled = true;
		}
		catch { }
	}

	private void Editor_DragOverHandler(object? sender, DragEventArgs e)
	{
		try
		{
			// allow drop visually; actual validation happens on Drop
			e.DragEffects = DragDropEffects.Copy;
			e.Handled = true;
		}
		catch
		{
			e.DragEffects = DragDropEffects.None;
			e.Handled = true;
		}
	}

	private void Editor_DropHandler(object? sender, DragEventArgs e)
	{
		try
		{
			_ = HandleDropAsync(e);
			e.Handled = true;
		}
		catch { }
	}

	private bool HasAnyDroppedFile(DragEventArgs e)
	{
		try
		{
			object? dataObj = null;
			try { dataObj = e.Data; } catch { }
			if (dataObj is null) return false;

			var getFileNames = dataObj.GetType().GetMethod("GetFileNames");
			if (getFileNames is not null)
			{
				var res = getFileNames.Invoke(dataObj, null);
				if (res is IEnumerable<string> names) return names.Any();
			}
		}
		catch
		{
			// ignore
		}
		return false;
	}

	/// <summary>
	/// 可注入的调试文件写入器，生产代码默认写入磁盘，测试中可替换为假实现。
	/// </summary>
	public IFileDebugWriter? FileDebugWriter { get; set; }

	/// <summary>
	/// 可注入的剪贴板服务，测试时可替换以便模拟剪贴板行为。
	/// </summary>
	public IClipboardService? ClipboardService { get; set; }

	private void OnEditorKeyDown(object? sender, KeyEventArgs e)
	{
		try
		{
			if (e.Key == Key.V && (e.KeyModifiers & KeyModifiers.Control) != 0)
			{
				_ = HandlePasteAsync();
				e.Handled = true;
			}
		}
		catch
		{
			// ignore
		}
	}

	private static byte[]? RunGetImageFromWin32ClipboardOnSta()
	{
		byte[]? result = null;
		var th = new Thread(() =>
		{
			try
			{
				result = AvaloniaClipboardService.GetImageFromWin32Clipboard();
			}
			catch (Exception ex)
			{
				try { Console.WriteLine($"[DEBUG] STA GetImageFromWin32Clipboard error: {ex.Message}"); } catch { }
			}
		});
		th.SetApartmentState(ApartmentState.STA);
		th.IsBackground = true;
		th.Start();
		if (!th.Join(5000)) { try { Console.WriteLine("[DEBUG] STA GetImageFromWin32Clipboard timed out"); } catch { } }
		return result;
	}

	public async Task HandlePasteAsync()
	{
		// 原先的防抖逻辑移至读取到 image 与捕获 caret 后进行，以便使用光标位置和图片长度作为去重键
		try
		{
			// debug: record paste attempt
			try { await FileDebugWriter?.WriteDebugFileAsync($"HandlePasteAsync invoked: {DateTime.Now:o}"); } catch { }
			try { Console.WriteLine($"[DEBUG] HandlePasteAsync invoked: {DateTime.Now:o}"); } catch { }
			byte[]? img = null;
			string? originalFileName = null;
			// Quick direct Win32 read (force), in case Avalonia/WinForms paths are blocked by Popup focus
			try
			{
				var win32Img = RunGetImageFromWin32ClipboardOnSta();
				if (win32Img is not null && win32Img.Length > 0)
				{
					Console.WriteLine($"[DEBUG] Direct Win32 STA read returned {win32Img.Length} bytes");
					img = win32Img;
				}
			}
			catch (Exception ex) { Console.WriteLine($"[DEBUG] Direct Win32 STA read failed: {ex.Message}"); }
			if (ClipboardService is null) return;
			// First try: use TopLevel's Clipboard (more reliable at runtime than Application.Current)
			try
			{
				var top = TopLevel.GetTopLevel(this);
				if (top?.Clipboard is not null)
				{
					try { Console.WriteLine($"[DEBUG] TopLevel.Clipboard is present: {top.Clipboard.GetType().FullName}"); } catch { }
					try
					{
						var cb = top.Clipboard;
						var mi = cb.GetType().GetMethod("GetImageAsync", Type.EmptyTypes) ?? cb.GetType().GetMethod("GetImage", Type.EmptyTypes);
						try { Console.WriteLine($"[DEBUG] TopLevel clipboard method: {(mi is null ? "<none>" : mi.Name)}"); } catch { }
						if (mi is not null)
						{
							if (mi.ReturnType == typeof(Task<byte[]>))
							{
								var t = mi.Invoke(cb, null) as Task<byte[]>;
								if (t is not null)
								{
									await t.ConfigureAwait(true);
									img = t.GetType().GetProperty("Result")?.GetValue(t) as byte[];
								}
							}
							else if (mi.ReturnType == typeof(byte[]))
							{
								img = mi.Invoke(cb, null) as byte[];
							}
						}
					}
					catch { }
				}
			}
			catch { }

			// Second try: injected clipboard service
			if (img is null || img.Length == 0)
			{
				try { img = await ClipboardService.GetImageAsync().ConfigureAwait(true); } catch { img = null; }
				// If still null, force Win32 read directly (less dependent on Avalonia/WinForms)
				if (img is null || img.Length == 0)
				{
					try { img = RunGetImageFromWin32ClipboardOnSta(); } catch { img = null; }
				}
			}
			try { await FileDebugWriter?.WriteDebugFileAsync($"Clipboard image length: {(img?.Length ?? 0)}"); } catch { }
			try { Console.WriteLine($"[DEBUG] Clipboard image length: {(img?.Length ?? 0)}"); } catch { }
			if (img is null || img.Length == 0) return;

			// fallback: if no binary image, try reading clipboard text (file:// or data: URI)
			if (img is null || img.Length == 0)
			{
				try
				{
					var txt = await ClipboardService.GetTextAsync().ConfigureAwait(true);
					try { await FileDebugWriter?.WriteDebugFileAsync($"Clipboard text length: {txt?.Length ?? 0}"); } catch { }
					try { Console.WriteLine($"[DEBUG] Clipboard text length: {txt?.Length ?? 0}"); } catch { }
					if (!string.IsNullOrWhiteSpace(txt))
					{
						// data: URI
						if (txt.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
						{
							var idx = txt.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
							if (idx >= 0)
							{
								var b64 = txt.Substring(idx + 7);
								try { img = Convert.FromBase64String(b64); } catch { }
							}
						}
						// file:// or plain path list
						if (img is null || img.Length == 0)
						{
							var lines = txt.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
							foreach (var l in lines)
							{
								var s = l.Trim();
								if (s.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
								{
									try { var path = new Uri(s).LocalPath; if (File.Exists(path)) { img = await File.ReadAllBytesAsync(path).ConfigureAwait(true); originalFileName = Path.GetFileName(path); break; } } catch { }
								}
								else if (File.Exists(s))
								{
									img = await File.ReadAllBytesAsync(s).ConfigureAwait(true);
									originalFileName = Path.GetFileName(s);
									break;
								}
							}
						}
					}
				}
				catch { }
			}

			// determine assets root based on active document directory when possible
			string? assetsRoot = null;
			try
			{
				// prefer DataContext's WorkspaceTabs (MainViewModel) to find the active document
				if (DataContext is Muse.ViewModels.MainViewModel mvm && mvm.WorkspaceTabs is not null && mvm.WorkspaceTabs.Length > 0)
				{
					var activeTab = mvm.WorkspaceTabs.FirstOrDefault(t => t.IsActive);
					if (activeTab is not null && !string.IsNullOrWhiteSpace(activeTab.FilePath))
					{
						var docDir = Path.GetDirectoryName(activeTab.FilePath) ?? Environment.CurrentDirectory;
						assetsRoot = Path.Combine(docDir, "assets");
					}
				}
				// fallback: resolve workspace service from container
				if (string.IsNullOrWhiteSpace(assetsRoot))
				{
					var ws = App.Resolve<Muse.Workspace.IWorkspaceService>();
					var state = ws?.GetState();
					var activeId = state?.ActiveDocumentId;
					var activeTab = state?.OpenTabs?.FirstOrDefault(t => string.Equals(t.DocumentId, activeId, StringComparison.Ordinal));
					if (activeTab is not null && !string.IsNullOrWhiteSpace(activeTab.FilePath))
					{
						var docDir = Path.GetDirectoryName(activeTab.FilePath) ?? Environment.CurrentDirectory;
						assetsRoot = Path.Combine(docDir, "assets");
					}
				}
			}
			catch { }

			// find editor textbox and capture caret/selection BEFORE awaiting file save to avoid race
			// prefer the editor that currently has keyboard focus (important for split views)
			TextBox? tb = null;
			try
			{
				var candidates = new[] { "EditorTextBox", "SplitSourceTextBox", "SplitSourceTextBoxV" }
					.Select(n => this.FindControl<TextBox>(n))
						.Where(x => x is not null)
						.Cast<TextBox>()
						.ToArray();
				// try to pick focused editor (do not change focus)
				var focused = candidates.FirstOrDefault(c => c.IsFocused);
				if (focused is not null)
				{
					tb = focused;
				}
				else if (candidates.Length > 0)
				{
					tb = candidates[0];
				}
			}
			catch { }

			// debug: record which editor was chosen and active document path
			try { var _ = FileDebugWriter?.WriteDebugFileAsync($"HandlePasteAsync chosen editor: {tb?.Name ?? "<none>"} (hash:{tb?.GetHashCode() ?? 0}), activeDoc={GetActiveDocumentPath() ?? "<unknown>"}"); } catch { }
			try { Console.WriteLine($"[DEBUG] HandlePasteAsync chosen editor: {tb?.Name ?? "<none>"} (hash:{tb?.GetHashCode() ?? 0}), activeDoc={GetActiveDocumentPath() ?? "<unknown>"}"); } catch { }
			int caret = 0;
			int selStart = 0;
			int selLength = 0;
			string beforeText = string.Empty;
			if (tb is not null)
			{
				caret = tb.CaretIndex;
				selStart = tb.SelectionStart;
				selLength = tb.SelectionEnd - tb.SelectionStart;
				beforeText = tb.Text ?? string.Empty;
			}

			var svc = string.IsNullOrWhiteSpace(assetsRoot) ? new AssetService() : new AssetService(assetsRoot);
			string ext = GetImageExtension(img) ?? "png";
			string name;
			if (!string.IsNullOrWhiteSpace(originalFileName))
			{
				var safe = Path.GetFileName(originalFileName);
				if (string.IsNullOrWhiteSpace(Path.GetExtension(safe))) safe = safe + "." + ext;
				name = safe;
			}
			else
			{
				name = $"pasted-{DateTime.Now:yyyyMMddHHmmss}.{ext}";
			}

			// 防抖去重：基于光标/选择与图片长度构建去重键，仅当完全相同的粘贴（同位置、同内容）在窗口期内才忽略
			try
			{
				lock (_pasteLock)
				{
					var now = DateTimeOffset.UtcNow;
					var imgLen = img?.Length ?? 0;
					var key = (Caret: caret, SelStart: selStart, SelLen: selLength, ImgLen: imgLen);
					if (_lastPasteAt.HasValue && (now - _lastPasteAt.Value) < PasteDuplicateWindow && key.Equals(_lastPasteKey))
					{
						try { var _ = FileDebugWriter?.WriteDebugFileAsync("HandlePasteAsync ignored duplicate"); } catch { }
						try { Console.WriteLine("[DEBUG] HandlePasteAsync ignored duplicate"); } catch { }
						return;
					}
					_lastPasteAt = now;
					_lastPasteKey = key;
				}
			}
			catch { }

			var rel = await svc.SaveImageAsync(img, name).ConfigureAwait(true);

			// Debug: record returned relative path and physical file existence
			try
			{
				string absPath;
				if (string.IsNullOrWhiteSpace(assetsRoot))
				{
					absPath = Path.GetFullPath(rel);
				}
				else
				{
					// rel is typically "assets/filename"; the actual file is written under assetsRoot (which is the full path to the assets folder)
					absPath = Path.GetFullPath(Path.Combine(assetsRoot, Path.GetFileName(rel)));
				}
				try { var _ = FileDebugWriter?.WriteDebugFileAsync($"SaveImage returned rel={rel}, abs={absPath}, exists={File.Exists(absPath)}, size={(File.Exists(absPath) ? new FileInfo(absPath).Length : -1)}"); } catch { }
				try { Console.WriteLine($"[DEBUG] SaveImage returned rel={rel}, abs={absPath}, exists={File.Exists(absPath)}, size={(File.Exists(absPath) ? new FileInfo(absPath).Length : -1)}"); } catch { }
			}
			catch { }

			if (tb is not null)
			{
				// perform insertion on UI thread against current text to avoid overwriting concurrent edits
				var toInsert = $"![]({rel})";
				try
				{
					try { var _ = FileDebugWriter?.WriteDebugFileAsync($"Paste debug: capturedCaret={caret}, selStart={selStart}, selLength={selLength}, imgLen={img?.Length ?? 0}, toInsertLen={toInsert.Length}"); } catch { }
					try { Console.WriteLine($"[DEBUG] Paste debug: capturedCaret={caret}, selStart={selStart}, selLength={selLength}, imgLen={img?.Length ?? 0}, toInsertLen={toInsert.Length}"); } catch { }

					// Use InvokeAsync to run on UI thread and wait for the insertion to complete.
					var __op = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
					{
						try
						{
							var curText = tb.Text ?? string.Empty;
							// prefer the editor's current caret/selection at insertion time
							int curCaret = tb.CaretIndex;
							int curSelStart = tb.SelectionStart;
							int curSelLength = tb.SelectionEnd - tb.SelectionStart;
							int insertPos = Math.Max(0, Math.Min(curText.Length, curCaret));
							if (curSelLength > 0 && curSelStart >= 0 && curSelStart <= curText.Length)
							{
								curText = curText.Remove(curSelStart, Math.Min(curSelLength, curText.Length - curSelStart));
								insertPos = Math.Max(0, Math.Min(curText.Length, curSelStart));
							}
							curText = curText.Insert(insertPos, toInsert);
							tb.Text = curText;
							// move caret after inserted text
							tb.CaretIndex = insertPos + toInsert.Length;
						}
						catch { }
					}, Avalonia.Threading.DispatcherPriority.Input);
					try { await __op; } catch { }
				}
				catch { }
			}
		}
		catch
		{
			// ignore paste failures
		}
	}

	private async Task HandleDropAsync(DragEventArgs e)
	{
		try
		{
			try { await FileDebugWriter?.WriteDebugFileAsync($"HandleDropAsync invoked: {DateTime.Now:o}"); } catch { }
			try { Console.WriteLine($"[DEBUG] HandleDropAsync invoked: {DateTime.Now:o}"); } catch { }
			// Use DataTransfer where available; fall back to deprecated Data for compatibility
			object? dataObj = e.Data;
			if (dataObj is null) return;
			IEnumerable<string>? fileNames = null;
			var tryNames = TryInvokeNoArgMethods(dataObj, "GetFileNames", "GetFileNamesAsync", "GetFiles", "GetFilesAsync");
			if (tryNames is not null) fileNames = tryNames;
			if (fileNames is null || !fileNames.Any())
			{
				var getTextMethod = dataObj.GetType().GetMethod("GetTextAsync") ?? dataObj.GetType().GetMethod("GetText");
				if (getTextMethod is not null)
				{
					try
					{
						if (getTextMethod.ReturnType == typeof(Task<string>))
						{
							var task = getTextMethod.Invoke(dataObj, null) as Task<string>;
							if (task is not null)
							{
								var txt = await task.ConfigureAwait(false);
								if (!string.IsNullOrEmpty(txt))
								{
									var lines = txt.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
									var uris = lines.Where(p => p.StartsWith("file://", StringComparison.OrdinalIgnoreCase)).Select(p => new Uri(p).LocalPath).ToArray();
									if (uris.Length > 0) fileNames = uris;
								}
							}
						}
						else if (getTextMethod.ReturnType == typeof(string))
						{
							var txt = getTextMethod.Invoke(dataObj, null) as string;
							if (!string.IsNullOrEmpty(txt))
							{
								var lines = txt.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
								var uris = lines.Where(p => p.StartsWith("file://", StringComparison.OrdinalIgnoreCase)).Select(p => new Uri(p).LocalPath).ToArray();
								if (uris.Length > 0) fileNames = uris;
							}
						}
					}
					catch { }
				}
			}

			if (fileNames is null) return;
			try { await FileDebugWriter?.WriteDebugFileAsync($"Drop filenames count: {fileNames?.Count() ?? 0}"); } catch { }
			try { Console.WriteLine($"[DEBUG] Drop filenames count: {fileNames?.Count() ?? 0}"); } catch { }

			// determine assets root based on active document directory when possible
			string? assetsRoot = null;
			try
			{
				if (DataContext is Muse.ViewModels.MainViewModel mvm && mvm.WorkspaceTabs is not null && mvm.WorkspaceTabs.Length > 0)
				{
					var activeTab = mvm.WorkspaceTabs.FirstOrDefault(t => t.IsActive);
					if (activeTab is not null && !string.IsNullOrWhiteSpace(activeTab.FilePath))
					{
						var docDir = Path.GetDirectoryName(activeTab.FilePath) ?? Environment.CurrentDirectory;
						assetsRoot = Path.Combine(docDir, "assets");
					}
				}
				if (string.IsNullOrWhiteSpace(assetsRoot))
				{
					var ws = App.Resolve<Muse.Workspace.IWorkspaceService>();
					var state = ws?.GetState();
					var activeId = state?.ActiveDocumentId;
					var activeTab = state?.OpenTabs?.FirstOrDefault(t => string.Equals(t.DocumentId, activeId, StringComparison.Ordinal));
					if (activeTab is not null && !string.IsNullOrWhiteSpace(activeTab.FilePath))
					{
						var docDir = Path.GetDirectoryName(activeTab.FilePath) ?? Environment.CurrentDirectory;
						assetsRoot = Path.Combine(docDir, "assets");
					}
				}
			}
			catch { }

			var svc = string.IsNullOrWhiteSpace(assetsRoot) ? new AssetService() : new AssetService(assetsRoot);

			var tb = this.FindControl<TextBox>("EditorTextBox") ?? this.FindControl<TextBox>("SplitSourceTextBox") ?? this.FindControl<TextBox>("SplitSourceTextBoxV");

			foreach (var f in fileNames)
			{
				try
				{
					if (!File.Exists(f)) continue;
					var bytes = await File.ReadAllBytesAsync(f).ConfigureAwait(true);
					var name = Path.GetFileName(f);
					// capture caret/selection before async save
					int caret = 0;
					int selStart = 0;
					int selLength = 0;
					string beforeText = string.Empty;
					if (tb is not null)
					{
						caret = tb.CaretIndex;
						selStart = tb.SelectionStart;
						selLength = tb.SelectionEnd - tb.SelectionStart;
						beforeText = tb.Text ?? string.Empty;
					}
					var rel = await svc.SaveImageAsync(bytes, name).ConfigureAwait(true);

					// Debug: record returned relative path and physical file existence for dropped file
					try
					{
						string absPath;
						if (string.IsNullOrWhiteSpace(assetsRoot))
						{
							absPath = Path.GetFullPath(rel);
						}
						else
						{
							absPath = Path.GetFullPath(Path.Combine(assetsRoot, Path.GetFileName(rel)));
						}
						try { var _ = FileDebugWriter?.WriteDebugFileAsync($"Drop SaveImage returned rel={rel}, abs={absPath}, exists={File.Exists(absPath)}, size={(File.Exists(absPath) ? new FileInfo(absPath).Length : -1)}"); } catch { }
						try { Console.WriteLine($"[DEBUG] Drop SaveImage returned rel={rel}, abs={absPath}, exists={File.Exists(absPath)}, size={(File.Exists(absPath) ? new FileInfo(absPath).Length : -1)}"); } catch { }
					}
					catch { }
					if (tb is not null)
					{
						var toInsert = $"![]({rel})";
						var text = beforeText;
						int insertPos = Math.Max(0, Math.Min(text.Length, caret));
						if (selLength > 0 && selStart >= 0 && selStart <= text.Length)
						{
							text = text.Remove(selStart, Math.Min(selLength, text.Length - selStart));
							insertPos = Math.Max(0, Math.Min(text.Length, selStart));
						}
						text = text.Insert(insertPos, toInsert);
						tb.Text = text;
						tb.CaretIndex = insertPos + toInsert.Length;
					}
				}
				catch
				{
					// ignore per-file failures
				}
			}
		}
		catch
		{
			// ignore
		}
	}

	private string[]? TryInvokeNoArgMethods(object? data, params string[] names)
	{
		if (data is null) return null;
		foreach (var name in names)
		{
			var m = data.GetType().GetMethod(name);
			if (m is null) continue;
			try
			{
				var r = m.Invoke(data, null);
				if (r is string[] sa && sa.Length > 0) return sa;
				if (r is IEnumerable<string> es)
				{
					var arr = es.ToArray();
					if (arr.Length > 0) return arr;
				}
			}
			catch { }
		}
		return null;
	}

	private static string? GetImageExtension(byte[]? data)
	{
		if (data == null || data.Length < 4) return null;
		// PNG
		if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) return "png";
		// JPEG
		if (data[0] == 0xFF && data[1] == 0xD8) return "jpg";
		// BMP (BM header)
		if (data[0] == 0x42 && data[1] == 0x4D) return "bmp";
		// GIF
		if (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46) return "gif";
		return null;
	}

	private void OnCopyErrorDetailsClick(object? sender, RoutedEventArgs e)
	{
		// fire-and-forget the async operation; tests call CopyErrorDetailsAsync directly and await it
		_ = CopyErrorDetailsAsync();
	}

	private async void OnBrowseDebugExportDirectoryClick(object? sender, RoutedEventArgs e)
	{
		if (DataContext is not MainViewModel vm)
		{
			return;
		}

		try
		{
			var topLevel = TopLevel.GetTopLevel(this);
			if (topLevel?.StorageProvider is null)
			{
				vm.SaveFeedbackIsError = true;
				vm.SaveFeedbackMessage = "当前平台不支持目录选择器，请手动输入目录。";
				return;
			}

			var options = new FolderPickerOpenOptions
			{
				Title = "选择错误详情调试导出目录",
				AllowMultiple = false
			};

			if (!string.IsNullOrWhiteSpace(vm.DebugExportDirectory))
			{
				try
				{
					var candidatePath = vm.DebugExportDirectory!;
					if (!Path.IsPathRooted(candidatePath))
					{
						candidatePath = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, candidatePath));
					}

					if (Directory.Exists(candidatePath))
					{
						options.SuggestedStartLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(candidatePath);
					}
				}
				catch
				{
					// Ignore invalid path issues and let the picker use system default.
				}
			}

			var selectedFolders = await topLevel.StorageProvider.OpenFolderPickerAsync(options);
			if (selectedFolders.Count == 0)
			{
				return;
			}

			var selectedPath = selectedFolders[0].TryGetLocalPath();
			if (string.IsNullOrWhiteSpace(selectedPath))
			{
				vm.SaveFeedbackIsError = true;
				vm.SaveFeedbackMessage = "所选目录无法转换为本地路径，请手动输入目录。";
				return;
			}

			vm.DebugExportDirectory = selectedPath;
			vm.SaveFeedbackIsError = false;
			vm.SaveFeedbackMessage = $"调试导出目录已更新：{selectedPath}";
		}
		catch (Exception ex)
		{
			vm.SaveFeedbackIsError = true;
			vm.SaveFeedbackMessage = $"选择目录失败：{ex.Message}";
		}
	}

	public async System.Threading.Tasks.Task CopyErrorDetailsAsync()
	{
		if (DataContext is not MainViewModel vm)
		{
			return;
		}

		var msg = vm.ConflictLogPreferenceSaveErrorMessage;
		if (string.IsNullOrWhiteSpace(msg))
		{
			return;
		}

		// 使用注入的剪贴板服务尝试写入剪贴板
		var copiedToClipboard = false;
		try
		{
			if (ClipboardService is not null)
			{
				copiedToClipboard = await ClipboardService.SetTextAsync(msg).ConfigureAwait(false);
			}
		}
		catch
		{
			// ignore and fall back to file
			copiedToClipboard = false;
		}

		// 无论是否成功复制到剪贴板，为了兼容测试与手动排查，通过抽象写入文件作为回退。
		try
		{
			string? outPath = null;
			if (FileDebugWriter is not null)
			{
				outPath = await FileDebugWriter.WriteDebugFileAsync(msg).ConfigureAwait(false);
			}
			else
			{
				// 运行时优先从容器解析，未初始化时再回退默认实现
				IFileDebugWriter writer;
				try
				{
					writer = App.Resolve<IFileDebugWriter>();
				}
				catch
				{
					writer = new FileDebugWriter();
				}
				outPath = await writer.WriteDebugFileAsync(msg).ConfigureAwait(false);
			}

			vm.SaveFeedbackIsError = false;
			if (copiedToClipboard)
			{
				vm.SaveFeedbackMessage = outPath is not null ? "错误详情已复制到剪贴板（并写入调试文件）。" : "错误详情已复制到剪贴板。";
			}
			else
			{
				vm.SaveFeedbackMessage = outPath is not null ? $"错误详情已写入：{outPath}" : "错误详情写入失败（回退不可用）。";
			}
		}
		catch
		{
			// ignore
		}
	}

	public async void OnLoadWorkspaceClick(object? sender, RoutedEventArgs e)
	{
		if (DataContext is not MainViewModel vm)
		{
			return;
		}

		try
		{
			var topLevel = TopLevel.GetTopLevel(this);
			if (topLevel?.StorageProvider is null)
			{
				vm.SaveFeedbackIsError = true;
				vm.SaveFeedbackMessage = "当前平台不支持工作区选择器。";
				return;
			}

			var options = new FolderPickerOpenOptions
			{
				Title = "选择工作区目录",
				AllowMultiple = false
			};

			try
			{
				if (!string.IsNullOrWhiteSpace(vm.WorkspaceRootDisplay))
				{
					var candidate = vm.WorkspaceRootDisplay!;
					if (!Path.IsPathRooted(candidate))
					{
						candidate = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, candidate));
					}
					if (Directory.Exists(candidate))
					{
						options.SuggestedStartLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(candidate).ConfigureAwait(true);
					}
				}
			}
			catch
			{
				// ignore suggested start location failures
			}

			var selected = await topLevel.StorageProvider.OpenFolderPickerAsync(options).ConfigureAwait(true);
			if (selected.Count == 0)
			{
				return;
			}

			var selectedPath = selected[0].TryGetLocalPath();
			if (string.IsNullOrWhiteSpace(selectedPath))
			{
				vm.SaveFeedbackIsError = true;
				vm.SaveFeedbackMessage = "所选目录无法转换为本地路径。";
				return;
			}

			vm.OpenWorkspaceAt(selectedPath);
			vm.SaveFeedbackIsError = false;
			vm.SaveFeedbackMessage = $"已加载工作区：{selectedPath}";
		}
		catch (Exception ex)
		{
			vm.SaveFeedbackIsError = true;
			vm.SaveFeedbackMessage = $"选择工作区失败：{ex.Message}";
		}
	}
}
