using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Muse.ViewModels;
using Muse.Services;

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
	}

	private Avalonia.Point? _dragStartPoint;
	private string? _draggingDocumentId;
	private bool _isDragging;
	private const double DragThreshold = 6.0; // pixels

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
		}
	}

	// DI constructor used in production when resolving via IServiceProvider
	public MainView(MainViewModel viewModel, IClipboardService clipboardService, IFileDebugWriter fileDebugWriter)
	{
		InitializeComponent();
		DataContext = viewModel;
		ClipboardService = clipboardService;
		FileDebugWriter = fileDebugWriter;
	}

	/// <summary>
	/// 可注入的调试文件写入器，生产代码默认写入磁盘，测试中可替换为假实现。
	/// </summary>
	public IFileDebugWriter? FileDebugWriter { get; set; }

	/// <summary>
	/// 可注入的剪贴板服务，测试时可替换以便模拟剪贴板行为。
	/// </summary>
	public IClipboardService? ClipboardService { get; set; }

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
}
