using System;
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
using Microsoft.Extensions.DependencyInjection;

namespace Muse;

public partial class App : Application
{
	private MainViewModel? _mainViewModel;
	// Expose the DI service provider for runtime resolution (static singleton)
	public static IServiceProvider? ServiceProvider { get; private set; }

	public static T Resolve<T>() where T : notnull
	{
		if (ServiceProvider is null)
		{
			throw new InvalidOperationException("应用服务容器尚未初始化。请在应用启动完成后再解析服务。");
		}

		return ServiceProvider.GetRequiredService<T>();
	}

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

			// Configure DI
			var services = new ServiceCollection();
			services.AddSingleton<IClipboardService, AvaloniaClipboardService>();
			services.AddSingleton<IFileDebugWriter, FileDebugWriter>();
			services.AddSingleton<MainViewModel>();
			services.AddSingleton<MainView>();

			var serviceProvider = services.BuildServiceProvider();
			// expose provider on Application instance for runtime resolution
			ServiceProvider = serviceProvider;

			_mainViewModel = Resolve<MainViewModel>();

			var mainWindow = new MainWindow();
			var mainView = Resolve<MainView>();
			mainWindow.Content = mainView;
			desktop.MainWindow = mainWindow;
		}
		else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
		{
			var services = new ServiceCollection();
			services.AddSingleton<IClipboardService, AvaloniaClipboardService>();
			services.AddSingleton<IFileDebugWriter, FileDebugWriter>();
			services.AddSingleton<MainViewModel>();
			services.AddSingleton<MainView>();

			var serviceProvider = services.BuildServiceProvider();
			ServiceProvider = serviceProvider;

			_mainViewModel = Resolve<MainViewModel>();
			var mv = Resolve<MainView>();
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
