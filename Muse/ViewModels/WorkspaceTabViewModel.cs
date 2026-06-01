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

	[ObservableProperty]
	private bool _isActive;

	public WorkspaceTabViewModel(WorkspaceTabState state, bool isActive)
	{
		DocumentId = state.DocumentId;
		FilePath = state.FilePath;
		FileName = state.FileName;
		IsDirty = state.IsDirty;
		HasExternalConflict = state.HasExternalConflict;
		IsActive = isActive;
	}
}
