using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Muse.ViewModels;

namespace Muse.Views;

public partial class MainView : UserControl
{
	public MainView()
	{
		InitializeComponent();
	}

	private async void OnCopyErrorDetailsClick(object? sender, RoutedEventArgs e)
	{
		if (DataContext is not MainViewModel vm)
		{
			return;
		}

		var msg = vm.ConflictLogPreferenceSaveErrorMessage;
		if (string.IsNullOrWhiteSpace(msg))
		{
			return;
		}

		try
		{
			var debugDir = System.IO.Path.Combine(Environment.CurrentDirectory, ".muse", "debug");
			System.IO.Directory.CreateDirectory(debugDir);
			var outPath = System.IO.Path.Combine(debugDir, "error-copy.txt");
			await System.IO.File.WriteAllTextAsync(outPath, msg);
			vm.SaveFeedbackIsError = false;
			vm.SaveFeedbackMessage = $"错误详情已写入：{outPath}";
		}
		catch
		{
			// ignore
		}
	}
}
