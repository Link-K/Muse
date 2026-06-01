using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Muse.Converters
{
	public class ExpandSymbolConverter : IValueConverter
	{
		public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
		{
			var expanded = "▾";
			var collapsed = "▸";
			try
			{
				var app = Avalonia.Application.Current;
				if (app?.Resources != null)
				{
					if (app.Resources.TryGetResource("ExpandExpandedGlyph", null, out var e))
						expanded = e as string ?? expanded;
					if (app.Resources.TryGetResource("ExpandCollapsedGlyph", null, out var c))
						collapsed = c as string ?? collapsed;
				}
			}
			catch
			{
				// ignore and fallback to defaults
			}

			if (value is bool b)
				return b ? expanded : collapsed;
			return collapsed;
		}

		public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
		{
			return Avalonia.Data.BindingOperations.DoNothing;
		}
	}
}
