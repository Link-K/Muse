using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Muse.ViewModels;

namespace Muse.Views;

public partial class MainView : UserControl
{
	public MainView()
	{
		InitializeComponent();
	}

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

		// 首先尝试将错误详情写入系统剪贴板（兼容多个 Avalonia 版本），
		// 若剪贴板不可用则回退到写入文件以保证可访问性（测试与手动排查）。
		var copiedToClipboard = false;
		try
		{
			// 1) 尝试通过 Application.Current.Clipboard（某些 Avalonia 版本）
			var appType = Type.GetType("Avalonia.Application, Avalonia");
			if (appType is not null)
			{
				var currentProp = appType.GetProperty("Current", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
				var current = currentProp?.GetValue(null);
				if (current is not null)
				{
					var clipProp = current.GetType().GetProperty("Clipboard");
					var clipboard = clipProp?.GetValue(current);
					if (clipboard is not null)
					{
						var setText = clipboard.GetType().GetMethod("SetTextAsync", new[] { typeof(string) });
						if (setText is not null)
						{
							var task = (System.Threading.Tasks.Task)setText.Invoke(clipboard, new object[] { msg })!;
							await task.ConfigureAwait(false);
							copiedToClipboard = true;
						}
					}
				}
			}
		}
		catch
		{
			// 忽略剪贴板尝试中的异常，继续回退策略
		}

		if (!copiedToClipboard)
		{
			try
			{
				// 2) 尝试通过 AvaloniaLocator.GetService<IClipboard>()（不同版本的 Avalonia）
				var locatorType = Type.GetType("Avalonia.AvaloniaLocator, Avalonia");
				if (locatorType is null)
				{
					// 部分版本命名空间不同，尝试 Avalonia.
					locatorType = Type.GetType("AvaloniaLocator, Avalonia");
				}
				if (locatorType is not null)
				{
					var currentProp = locatorType.GetProperty("Current", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
					var current = currentProp?.GetValue(null);
					var getService = locatorType.GetMethod("GetService", new[] { typeof(Type) });
					if (getService is not null && current is not null)
					{
						// 尝试以类型名获取 IClipboard 类型
						var clipboardType = Type.GetType("Avalonia.Input.IClipboard, Avalonia.Input")
							?? Type.GetType("Avalonia.Input.IClipboard, Avalonia");
						if (clipboardType is not null)
						{
							var clipboard = getService.Invoke(current, new object[] { clipboardType });
							if (clipboard is not null)
							{
								var setText = clipboard.GetType().GetMethod("SetTextAsync", new[] { typeof(string) });
								if (setText is not null)
								{
									var task = (System.Threading.Tasks.Task)setText.Invoke(clipboard, new object[] { msg })!;
									await task.ConfigureAwait(false);
									copiedToClipboard = true;
								}
							}
						}
					}
				}
			}
			catch
			{
				// 忽略
			}
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
