using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Muse.Converters
{
	public class ExpandSymbolConverter : IValueConverter
	{
		private static string s_expanded = "▾";
		private static string s_collapsed = "▸";
		private static bool s_initialized = false;
		private static readonly object s_initLock = new object();

		public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
		{
			// Lazy initialize glyphs once to avoid repeated resource lookups
			if (!s_initialized)
			{
				lock (s_initLock)
				{
					if (!s_initialized)
					{
						try
						{
							var app = Avalonia.Application.Current;
							if (app?.Resources != null)
							{
								if (app.Resources.TryGetResource("ExpandExpandedGlyph", null, out var e))
									s_expanded = e as string ?? s_expanded;
								if (app.Resources.TryGetResource("ExpandCollapsedGlyph", null, out var c))
									s_collapsed = c as string ?? s_collapsed;
							}
						}
						catch
						{
							// best-effort only
						}
						s_initialized = true;
					}
				}
			}

			if (value is bool b)
				return b ? s_expanded : s_collapsed;
			return s_collapsed;
		}

		public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
		{
			return Avalonia.Data.BindingOperations.DoNothing;
		}
	}
}
