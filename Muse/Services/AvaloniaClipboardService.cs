using System;
using System.Threading.Tasks;

namespace Muse.Services
{
	/// <summary>
	/// 尝试使用 Avalonia 可用的多种路径写入剪贴板，兼容不同版本。
	/// </summary>
	public class AvaloniaClipboardService : IClipboardService
	{
		public async Task<bool> SetTextAsync(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				return false;

			try
			{
				// 1) Application.Current.Clipboard
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
								var task = (Task)setText.Invoke(clipboard, new object[] { text })!;
								await task.ConfigureAwait(false);
								return true;
							}
						}
					}
				}
			}
			catch
			{
				// ignore and fallthrough
			}

			try
			{
				// 2) AvaloniaLocator / Avalonia.AvaloniaLocator GetService(IClipboard)
				var locatorType = Type.GetType("Avalonia.AvaloniaLocator, Avalonia") ?? Type.GetType("AvaloniaLocator, Avalonia");
				if (locatorType is not null)
				{
					var currentProp = locatorType.GetProperty("Current", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
					var current = currentProp?.GetValue(null);
					var getService = locatorType.GetMethod("GetService", new[] { typeof(Type) });
					if (getService is not null && current is not null)
					{
						var clipboardType = Type.GetType("Avalonia.Input.IClipboard, Avalonia.Input") ?? Type.GetType("Avalonia.Input.IClipboard, Avalonia");
						if (clipboardType is not null)
						{
							var clipboard = getService.Invoke(current, new object[] { clipboardType });
							if (clipboard is not null)
							{
								var setText = clipboard.GetType().GetMethod("SetTextAsync", new[] { typeof(string) });
								if (setText is not null)
								{
									var task = (Task)setText.Invoke(clipboard, new object[] { text })!;
									await task.ConfigureAwait(false);
									return true;
								}
							}
						}
					}
				}
			}
			catch
			{
				// ignore
			}

			return false;
		}
	}
}
