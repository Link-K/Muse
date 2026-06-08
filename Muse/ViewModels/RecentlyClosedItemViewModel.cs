using System.Windows.Input;

namespace Muse.ViewModels;

public sealed class RecentlyClosedItemViewModel
{
	public RecentlyClosedItemViewModel(string filePath, string fileName, ICommand reopenCommand, ICommand removeCommand)
	{
		FilePath = filePath;
		FileName = fileName;
		ReopenCommand = reopenCommand;
		RemoveCommand = removeCommand;
	}

	public string FilePath { get; }
	public string FileName { get; }
	public ICommand ReopenCommand { get; }
	public ICommand RemoveCommand { get; }
}
