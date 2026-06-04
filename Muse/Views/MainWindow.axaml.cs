using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;

namespace Muse.Views;

public partial class MainWindow : Window
{
	public MainWindow()
	{
		InitializeComponent();

		try
		{
			// Keep the existing KeyDown handler as a best-effort fallback
			this.AddHandler(KeyDownEvent, (sender, e) =>
			{
				try
				{
					if (e.Key == Key.V && (e.KeyModifiers & KeyModifiers.Control) != 0)
					{
						var mv = this.Content as MainView;
						if (mv is not null)
						{
							_ = mv.HandlePasteAsync();
							e.Handled = true;
						}
					}
				}
				catch { }
			}, RoutingStrategies.Tunnel, handledEventsToo: true);

			// Start low-level keyboard hook to reliably detect Ctrl+V when our app is foreground.
			try
			{
				Muse.Services.KeyboardHook.CtrlVPressed += async () =>
				{
					try
					{
						var mv = this.Content as MainView;
						if (mv is not null)
						{
							await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () => await mv.HandlePasteAsync().ConfigureAwait(false)).ConfigureAwait(false);
						}
					}
					catch { }
				};
				Muse.Services.KeyboardHook.Start();
			}
			catch { }
		}
		catch { }
	}

	protected override void OnClosed(EventArgs e)
	{
		try { Muse.Services.KeyboardHook.Stop(); } catch { }
		base.OnClosed(e);
	}
}
