using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Muse.ViewModels;

public partial class FileTreeNodeViewModel : ObservableObject
{
	public FileTreeNodeViewModel(string path, string name, bool isDirectory, Action<FileTreeNodeViewModel>? openAction = null)
	{
		Path = path;
		Name = name;
		IsDirectory = isDirectory;
		Children = new ObservableCollection<FileTreeNodeViewModel>();
		_openAction = openAction;
	}

	private readonly Action<FileTreeNodeViewModel>? _openAction;

	public string Path { get; }
	public string Name { get; }
	public bool IsDirectory { get; }

	public string IconGlyph => IsDirectory ? "\uE8B7" : "\uE8A4";

	[ObservableProperty]
	private bool _isExpanded;

	public ObservableCollection<FileTreeNodeViewModel> Children { get; }

	[RelayCommand]
	private void ToggleExpanded()
	{
		if (!IsDirectory) return;
		IsExpanded = !IsExpanded;
	}

	[RelayCommand]
	private void Open()
	{
		_openAction?.Invoke(this);
	}
}
