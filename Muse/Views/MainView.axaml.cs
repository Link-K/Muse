using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Muse.ViewModels;
using Muse.Services;

namespace Muse.Views;

public partial class MainView : UserControl
{
	// Parameterless ctor kept for design-time compatibility; runtime should use DI constructor below.
	public MainView()
	{
		InitializeComponent();
		// Design-time / fallback defaults
		ClipboardService = new AvaloniaClipboardService();
		FileDebugWriter = new FileDebugWriter();
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
				// 保持向后兼容：若未注入写入器，使用默认实现
				var writer = new FileDebugWriter();
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
