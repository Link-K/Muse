using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Diagnostics;
using System.Linq;
using Avalonia.Markup.Xaml;
using Muse.ViewModels;
using Muse.Views;

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
			desktop.MainWindow = new MainWindow
			{
				DataContext = _mainViewModel
			};
		}
		else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
		{
			_mainViewModel = new MainViewModel();
			singleViewPlatform.MainView = new MainView
			{
				DataContext = _mainViewModel
			};
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
