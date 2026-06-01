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
			}
			catch
			{
				// best-effort only
			}
		}
	}
}
