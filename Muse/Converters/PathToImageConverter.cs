using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using System;
using System.Globalization;
using System.IO;

namespace Muse.Converters;

public sealed class PathToImageConverter : IValueConverter
{
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo? culture)
	{
		try
		{
			if (value is string path && !string.IsNullOrWhiteSpace(path))
			{
				try { Console.WriteLine($"[DEBUG] PathToImageConverter.Convert path={path}, exists={File.Exists(path)}"); } catch { }
				if (File.Exists(path))
				{
					// Read bytes and create bitmap from memory stream to avoid locked file handles
					var bytes = File.ReadAllBytes(path);
					var ms = new MemoryStream(bytes);
					try
					{
						// Ensure Bitmap is created on UI thread to avoid cross-thread rendering issues
						if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
						{
							var task = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => new Bitmap(new MemoryStream(bytes)));
							return task.GetAwaiter().GetResult();
						}
						return new Bitmap(ms);
					}
					catch (Exception ex)
					{
						try { Console.WriteLine($"[DEBUG] PathToImageConverter.Bitmap create exception: {ex}"); } catch { }
						throw;
					}
				}
			}
		}
		catch (Exception ex)
		{
			try { Console.WriteLine($"[DEBUG] PathToImageConverter.Convert exception: {ex}"); } catch { }
			// fallback
		}

		return null;
	}

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo? culture)
	{
		throw new NotSupportedException();
	}
}
