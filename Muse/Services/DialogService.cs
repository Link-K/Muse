using Avalonia.Controls;
using System;
using System.Threading.Tasks;

namespace Muse.Services
{
	// DialogService accepts a resolver for the owner Window so it can be registered
	// before the application's main window exists.
	public class DialogService : IDialogService
	{
		private readonly Func<Window?> _ownerResolver;

		public DialogService(Func<Window?> ownerResolver)
		{
			_ownerResolver = ownerResolver ?? (() => null);
		}

		public async Task ShowMessageAsync(string title, string message)
		{
			var owner = _ownerResolver();
			var panel = new StackPanel { Margin = new Avalonia.Thickness(12) };
			var text = new TextBlock { Text = message ?? string.Empty };
			// Try to set wrapping if available
			var twProp = text.GetType().GetProperty("TextWrapping");
			if (twProp is not null)
			{
				try
				{
					var enumType = twProp.PropertyType;
					var val = Enum.Parse(enumType, "Wrap");
					twProp.SetValue(text, val);
				}
				catch { }
			}

			panel.Children.Add(text);
			var ok = new Button { Content = "确定", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, MinWidth = 80, Margin = new Avalonia.Thickness(0, 12, 0, 0) };
			panel.Children.Add(ok);

			var dlg = new Window
			{
				Title = title,
				Content = panel,
				SizeToContent = SizeToContent.WidthAndHeight,
				CanResize = false,
				WindowStartupLocation = WindowStartupLocation.CenterOwner
			};

			ok.Click += (_, _) => dlg.Close();

			if (owner is not null)
			{
				await dlg.ShowDialog(owner);
			}
			else
			{
				dlg.Show();
				await Task.CompletedTask;
			}
		}
	}
}
