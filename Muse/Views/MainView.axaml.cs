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
	public MainView()
	{
		InitializeComponent();
		ClipboardService = new AvaloniaClipboardService();
	}

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

		// 无论是否成功复制到剪贴板，为了兼容测试与手动排查，仍写入文件作为回退（不会覆盖用户期望行为）
		try
		{
			var debugDir = System.IO.Path.Combine(Environment.CurrentDirectory, ".muse", "debug");
			System.IO.Directory.CreateDirectory(debugDir);
			var outPath = System.IO.Path.Combine(debugDir, "error-copy.txt");
			await System.IO.File.WriteAllTextAsync(outPath, msg).ConfigureAwait(false);
			vm.SaveFeedbackIsError = false;
			vm.SaveFeedbackMessage = copiedToClipboard ? "错误详情已复制到剪贴板（并写入调试文件）。" : $"错误详情已写入：{outPath}";
		}
		catch
		{
			// ignore
		}
	}
}
