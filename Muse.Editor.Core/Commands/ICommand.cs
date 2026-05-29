using Muse.Editor.Core.Documents;

namespace Muse.Editor.Core.Commands;

public interface ICommand
{
	string Name { get; }

	bool CanExecute(IDocumentModel document);
}
