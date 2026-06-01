using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Muse.Tests
{
	internal static class TestModuleInitializer
	{
		[ModuleInitializer]
		public static void Initialize()
		{
			try
			{
				// Ensure Application.Current exists
				if (Application.Current == null)
				{
					// Create App instance (will call Initialize in App when needed)
					var app = new Muse.App();
					// Reflection fallback if Application.Current still null
					if (Application.Current == null)
					{
						var prop = typeof(Application).GetProperty("Current", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
						prop?.SetValue(null, app);
					}
				}

				// Load theme resources from embedded avares URI and merge into application's resources
				try
				{
					var uri = new Uri("avares://Muse.ThemeUX/ThemeResources.xaml");
					var rd = AvaloniaXamlLoader.Load(uri) as ResourceDictionary;
					if (rd is not null)
					{
						Application.Current.Resources.MergedDictionaries.Add(rd);
					}
				}
				catch
				{
					// ignore resource load failures in test environments
				}

				// Ensure Avalonia platform is initialized for controls that rely on platform services
				try
				{
					// This will perform platform detection and minimal setup without starting an app loop.
					Avalonia.AppBuilder.Configure<Muse.App>()
						.UsePlatformDetect()
						.SetupWithoutStarting();

					// GridSplitter requires ICursorFactory, so we bind a dummy implementation if not already bound
					if (AvaloniaLocator.Current.GetService<Avalonia.Platform.ICursorFactory>() == null)
					{
						AvaloniaLocator.CurrentMutable.Bind<Avalonia.Platform.ICursorFactory>().ToConstant(new TestCursorFactory());
					}
				}
				catch
				{
					// best-effort only; if platform can't be initialized in the test environment,
					// tests should continue to run with fallback behavior.
				}
			}
			catch
			{
				// best-effort only
			}
		}
	}
}
