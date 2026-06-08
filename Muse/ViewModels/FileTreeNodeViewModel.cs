using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Muse.Workspace;

namespace Muse.ViewModels;

public partial class FileTreeNodeViewModel : ObservableObject
{
	public FileTreeNodeViewModel(
		string path,
		string name,
		bool isDirectory,
		Action<FileTreeNodeViewModel>? openAction = null,
		Func<string, string, bool, WorkspaceMutationResult>? createAction = null,
		Func<string, string, WorkspaceMutationResult>? renameAction = null,
		Func<string, WorkspaceMutationResult>? removeAction = null,
		Func<string, string, WorkspaceMutationResult>? moveAction = null,
		Func<string, WorkspaceMutationResult>? closeAndRemoveAction = null,
		Func<string, bool>? copyRelativePathAction = null,
		Action<FileTreeNodeViewModel>? cancelEditingAction = null)
	{
		Path = path;
		Name = name;
		IsDirectory = isDirectory;
		Children = new ObservableCollection<FileTreeNodeViewModel>();
		_openAction = openAction;
		_createAction = createAction;
		_renameAction = renameAction;
		_removeAction = removeAction;
		_moveAction = moveAction;
		_closeAndRemoveAction = closeAndRemoveAction;
		_copyRelativePathAction = copyRelativePathAction;
		_cancelEditingAction = cancelEditingAction;
	}

	private readonly Action<FileTreeNodeViewModel>? _openAction;
	private readonly Func<string, string, bool, WorkspaceMutationResult>? _createAction;
	private readonly Func<string, string, WorkspaceMutationResult>? _renameAction;
	private readonly Func<string, WorkspaceMutationResult>? _removeAction;
	private readonly Func<string, string, WorkspaceMutationResult>? _moveAction;
	private readonly Func<string, WorkspaceMutationResult>? _closeAndRemoveAction;
	private readonly Func<string, bool>? _copyRelativePathAction;
	private readonly Action<FileTreeNodeViewModel>? _cancelEditingAction;

	public string Path { get; }
	public string Name { get; }
	public bool IsDirectory { get; }

	public string IconGlyph => IsDirectory ? "\uE8B7" : "\uE8A4";

	[ObservableProperty]
	private bool _isExpanded;

	[ObservableProperty]
	private string? _lastError;

	[ObservableProperty]
	private bool _isEditing;

	[ObservableProperty]
	private string _editingName = string.Empty;

	public ObservableCollection<FileTreeNodeViewModel> Children { get; }

	public bool CanCreate => _createAction is not null;
	public bool CanRename => _renameAction is not null;
	public bool CanRemove => _removeAction is not null || _closeAndRemoveAction is not null;
	public bool CanMove => _moveAction is not null;
	public bool CanCloseAndRemove => _closeAndRemoveAction is not null;

	[RelayCommand]
	private void Open() => _openAction?.Invoke(this);

	[RelayCommand]
	private void ToggleExpanded()
	{
		if (!IsDirectory) return;
		IsExpanded = !IsExpanded;
	}

	[RelayCommand]
	private void StartEditing() => BeginRename();

	public void BeginRename()
	{
		IsEditing = true;
		EditingName = Name;
		LastError = null;
	}

	[RelayCommand]
	private void CommitEditing()
	{
		if (!IsEditing || _renameAction is null) return;
		var result = _renameAction(Path, EditingName);
		if (result.Succeeded)
		{
			IsEditing = false;
			LastError = null;
		}
		else
		{
			LastError = result.Message;
		}
	}

	[RelayCommand]
	private void CancelEditing()
	{
		IsEditing = false;
		LastError = null;
		_cancelEditingAction?.Invoke(this);
	}

	[RelayCommand]
	private void Delete()
	{
		if (_closeAndRemoveAction is not null)
		{
			var result = _closeAndRemoveAction(Path);
			if (!result.Succeeded)
				LastError = result.Message;
		}
		else if (_removeAction is not null)
		{
			var result = _removeAction(Path);
			LastError = result.Succeeded ? null : result.Message;
		}
	}

	[RelayCommand]
	private void CreateFile(string? name)
	{
		if (_createAction is null) return;
		var fileName = string.IsNullOrWhiteSpace(name) ? "new-doc.md" : name;
		var parentPath = IsDirectory ? Path : System.IO.Path.GetDirectoryName(Path) ?? Path;
		var result = _createAction(parentPath, fileName, false);
		LastError = result.Succeeded ? null : result.Message;
	}

	[RelayCommand]
	private void CreateDirectory(string? name)
	{
		if (_createAction is null) return;
		var dirName = string.IsNullOrWhiteSpace(name) ? "new-folder" : name;
		var parentPath = IsDirectory ? Path : System.IO.Path.GetDirectoryName(Path) ?? Path;
		var result = _createAction(parentPath, dirName, true);
		LastError = result.Succeeded ? null : result.Message;
	}

	[RelayCommand]
	private void OpenInExplorer()
	{
		try
		{
			var fullPath = System.IO.Path.GetFullPath(Path);
			if (IsDirectory)
			{
				Process.Start(new ProcessStartInfo { FileName = fullPath, UseShellExecute = true });
			}
			else
			{
				Process.Start("explorer.exe", $"/select,\"{fullPath}\"");
			}
		}
		catch
		{
			// ignore explorer launch failures
		}
	}

	[RelayCommand]
	private void CopyRelativePath()
	{
		if (_copyRelativePathAction is null) return;
		if (!_copyRelativePathAction(Path))
			LastError = "复制相对路径失败。";
		else
			LastError = null;
	}
}
