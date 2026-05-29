using Muse.Editor.Core.Commands;
using Muse.Editor.Core.Documents;

namespace Muse.Editor.Core.Sessions;

public interface IEditorSession
{
	string SessionId { get; }

	IDocumentModel Document { get; }

	bool CanUndo { get; }

	bool CanRedo { get; }

	CommandExecutionResult Execute(ICommand command);

	CommandExecutionResult Undo();

	CommandExecutionResult Redo();
}
