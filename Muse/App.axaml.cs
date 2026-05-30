using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Diagnostics;
using System.Linq;
using Avalonia.Markup.Xaml;
using Muse.ViewModels;
using Muse.Views;
using Muse.Services;

namespace Muse;

public partial class App : Application
{
	private MainViewModel? _mainViewModel;

	public override void Initialize()
	{
		AvaloniaXamlLoader.Load(this);
	}

	public override void OnFrameworkInitializationCompleted()
	{
		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			// Avoid duplicate validations from both Avalonia and the CommunityToolkit.
			// More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
			DisableAvaloniaDataAnnotationValidation();
			_mainViewModel = new MainViewModel();
			var clipboardService = new AvaloniaClipboardService();
			var mainWindow = new MainWindow
			{
				DataContext = _mainViewModel
			};
			// Try to locate the MainView instance inside the window to inject the clipboard service
			// MainWindow's content is the MainView instance defined in XAML
			if (mainWindow.Content is MainView mvRoot)
			{
				mvRoot.ClipboardService = clipboardService;
			}
			desktop.MainWindow = mainWindow;
		}
		else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
		{
			_mainViewModel = new MainViewModel();
			var clipboardService = new AvaloniaClipboardService();
			var mv = new MainView
			{
				DataContext = _mainViewModel,
				ClipboardService = clipboardService
			};
			singleViewPlatform.MainView = mv;
		}

		if (ApplicationLifetime is IControlledApplicationLifetime controlledLifetime)
		{
			controlledLifetime.Exit += (_, _) =>
			{
				_mainViewModel?.FlushConflictLogPreferencesNow();
				if (_mainViewModel is not null)
				{
					Debug.WriteLine($"[ConflictLogPref] Exit summary attempts={_mainViewModel.DebugConflictLogFlushAttemptCount}, failures={_mainViewModel.DebugConflictLogFlushFailureCount}, lastError={_mainViewModel.DebugLastConflictLogFlushError ?? "none"}");
				}
				_mainViewModel?.Dispose();
			};
		}

		base.OnFrameworkInitializationCompleted();
	}

	private void DisableAvaloniaDataAnnotationValidation()
	{
		// Get an array of plugins to remove
		var dataValidationPluginsToRemove =
			BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

		// remove each entry found
		foreach (var plugin in dataValidationPluginsToRemove)
		{
			BindingPlugins.DataValidators.Remove(plugin);
		}
	}
}
