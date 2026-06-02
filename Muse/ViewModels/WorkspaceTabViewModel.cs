using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Muse.Workspace;

namespace Muse.ViewModels;

public sealed partial class WorkspaceTabViewModel : ObservableObject
{
	public string DocumentId { get; }
	public string FilePath { get; }
	public string FileName { get; }
	public bool IsDirty { get; }
	public bool HasExternalConflict { get; }

	public System.Windows.Input.ICommand? ActivateCommand { get; }

	public System.Windows.Input.ICommand? CloseCommand { get; }

	[ObservableProperty]
	private bool _isActive;

	[ObservableProperty]
	private bool _isDropTarget;

	[ObservableProperty]
	private bool _isDropBefore;

	[ObservableProperty]
	private bool _isDropAfter;

	public WorkspaceTabViewModel(WorkspaceTabState state, bool isActive)
	{
		DocumentId = state.DocumentId;
		FilePath = state.FilePath;
		FileName = state.FileName;
		IsDirty = state.IsDirty;
		HasExternalConflict = state.HasExternalConflict;
		IsActive = isActive;
	}

	public WorkspaceTabViewModel(WorkspaceTabState state, bool isActive, System.Windows.Input.ICommand? activateCommand, System.Windows.Input.ICommand? closeCommand)
		: this(state, isActive)
	{
		ActivateCommand = activateCommand;
		CloseCommand = closeCommand;
	}
}
